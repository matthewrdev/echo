using Echo.Models;
using Echo.Projects;
using Echo.Telemetry;
using System.Text.Json;

namespace Echo.Pipeline.Runs;

public sealed class RunStateRecovery : IRunStateRecovery
{
    private readonly IRunContextFactory contextFactory;
    private readonly IPerformanceTraceStore traces;

    public RunStateRecovery(IRunContextFactory contextFactory, IPerformanceTraceStore traces)
    {
        this.contextFactory = contextFactory;
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
            foreach (var summary in store.ListRuns().Where(run => run.Status == "running"))
            {
                var run = store.GetRun(summary.Id);
                var directory = store.PathFor($"runs/{run.Id}");
                var cancelled = File.Exists(store.PathFor($"runs/{run.Id}/cancel"));
                using var context = contextFactory.Create(store, run, CancellationToken.None, null);
                try
                {
                    traces.RecoverInterrupted(directory, cancelled);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    context.Report("warning", new Dictionary<string, object?>
                    {
                        ["message"] = "Interrupted performance trace could not be recovered: " + error.Message
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
