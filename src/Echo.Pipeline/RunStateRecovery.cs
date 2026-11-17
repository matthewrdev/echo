using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Telemetry;
using System.Text.Json;

namespace Echo.Pipeline;

[Export(typeof(IRunStateRecovery)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RunStateRecovery : IRunStateRecovery
{
    private readonly IRunContextFactory contextFactory;
    private readonly IJsonCodec jsonCodec;
    private readonly IPerformanceTraceStore traces;

    [ImportingConstructor]
    public RunStateRecovery(IRunContextFactory contextFactory, IJsonCodec jsonCodec, IPerformanceTraceStore traces)
    {
        this.contextFactory = contextFactory;
        this.jsonCodec = jsonCodec;
        this.traces = traces;
    }

    public void Recover(IProjectStore store)
    {
        FileStream lease;
        try
        {
            lease = store.Lock("run");
        }
        catch (EchoException error) when (error.ExitCode == 6)
        {
            // A live processor owns this lease, even when it has not reported recently.
            return;
        }

        using (lease)
        {
            var directory = store.PathFor("runs");
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "run.json", SearchOption.AllDirectories))
            {
                var run = store.GetRun(Path.GetFileName(Path.GetDirectoryName(path))!);
                if (run.Status != "running")
                {
                    continue;
                }

                var cancelled = File.Exists(store.PathFor($"runs/{run.Id}/cancel"));
                using var context = contextFactory.Create(store, run, CancellationToken.None, null);
                try
                {
                    traces.RecoverInterrupted(Path.GetDirectoryName(path)!, cancelled);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    context.Report("warning", new
                    {
                        message = "Interrupted performance trace could not be recovered: " + error.Message
                    });
                }
                if (cancelled)
                {
                    context.Cancel();
                }
                else
                {
                    context.Fail(new EchoException("The processing worker exited unexpectedly. Saved checkpoints are retained. Resume this run to continue from completed work."));
                }
            }
        }
    }
}
