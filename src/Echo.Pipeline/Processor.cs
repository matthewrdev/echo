using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

[Export(typeof(IProcessor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class Processor : IProcessor
{
    private readonly IReadOnlyList<IPipelineStage> stages;
    private readonly IRunContextFactory contextFactory;
    private readonly IFileOperations fileOperations;

    [ImportingConstructor]
    public Processor([ImportMany] IEnumerable<IPipelineStage> stages,
        IRunContextFactory contextFactory,
        IFileOperations fileOperations)
    {
        this.stages = stages.OrderBy(stage => stage.Order).ToArray();
        this.contextFactory = contextFactory;
        this.fileOperations = fileOperations;
    }

    public IReadOnlyList<string> Stages => stages.Select(stage => stage.Name).ToArray();

    public RunData Execute(IProjectStore store,
        string target,
        CancellationToken token,
        string? resumeId = null,
        Action<object>? output = null)
    {
        using var lease = store.Lock("run");
        var run = resumeId == null
            ? CreateRun(store, target)
            : store.GetRun(resumeId);

        if (!Stages.Contains(run.Target))
        {
            throw new EchoException("Unknown target: " + run.Target, 2);
        }

        run.Snapshot.Settings.Validate();
        var cancelFile = store.PathFor($"runs/{run.Id}/cancel");
        if (File.Exists(cancelFile))
        {
            File.Delete(cancelFile);
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var timer = new Timer(_ =>
        {
            if (File.Exists(cancelFile))
            {
                cancellation.Cancel();
            }
        }, null, 0, 200);
        var context = contextFactory.Create(store, run, cancellation.Token, output);
        context.Start();
        try
        {
            context.ReportProgress(run.Target, "Checking source photographs and reusable saved work before processing.");
            ValidateSources(context);
            foreach (var stage in stages)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                stage.Execute(context);
                if (stage.Name == run.Target)
                {
                    context.Complete();
                    return run;
                }
            }

            throw new EchoException("No processor was composed for target: " + run.Target);
        }
        catch (OperationCanceledException)
        {
            context.Cancel();
            throw;
        }
        catch (Exception error)
        {
            context.Fail(error);
            throw;
        }
    }

    private RunData CreateRun(IProjectStore store, string target)
    {
        return new RunData
        {
            Id = Guid.NewGuid().ToString("N"),
            Snapshot = store.Load(),
            Target = target
        };
    }

    private void ValidateSources(RunContext context)
    {
        if (context.Images.Count < 2)
        {
            throw new EchoException("Enable at least two images before running.");
        }

        foreach (var image in context.Images)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var path = context.Store.PathFor(image.Source);
            var sourceIsValid = File.Exists(path) && fileOperations.HashFile(path) == image.SourceHash;
            if (!sourceIsValid)
            {
                throw new EchoException($"Image source changed or is missing: {image.Name}. Reimport into a new project to preserve checkpoint provenance.");
            }
        }
    }
}
