using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(IRunStateRecovery)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RunStateRecovery : IRunStateRecovery
{
    private readonly IRunContextFactory contextFactory;
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public RunStateRecovery(IRunContextFactory contextFactory, IJsonCodec jsonCodec)
    {
        this.contextFactory = contextFactory;
        this.jsonCodec = jsonCodec;
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
                var run = jsonCodec.Read<RunData>(path);
                if (run.Status != "running")
                {
                    continue;
                }

                var context = contextFactory.Create(store, run, CancellationToken.None, null);
                if (File.Exists(store.PathFor($"runs/{run.Id}/cancel")))
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
