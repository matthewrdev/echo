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

        if (run.StorageVersion != 3)
        {
            throw new EchoException("This run uses obsolete artifact storage. Start a new run to reprocess the source photographs.", 2);
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
        using var context = contextFactory.Create(store, run, cancellation.Token, output);
        context.Start();
        using var resourceMonitor = new RunResourceMonitor(context, cancellation);
        try
        {
            context.ReportProgress(run.Target, "Checking source photographs and reusable saved work before processing.");
            using (var validation = context.Trace.Begin("Validate source photographs", PerformanceSpanKind.Operation))
            {
                try
                {
                    ValidateSources(context);
                }
                catch (OperationCanceledException)
                {
                    validation?.Complete(PerformanceStatus.Cancelled);
                    throw;
                }
                catch
                {
                    validation?.Complete(PerformanceStatus.Failed);
                    throw;
                }
            }
            foreach (var stage in stages)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                ExecuteStage(context, stage);
                if (stage.Name == run.Target)
                {
                    resourceMonitor.Dispose();
                    if (resourceMonitor.Failure is Exception resourceError)
                    {
                        throw resourceError;
                    }
                    context.Complete();
                    return run;
                }
            }

            throw new EchoException("No processor was composed for target: " + run.Target);
        }
        catch (OperationCanceledException)
        {
            resourceMonitor.Dispose();
            if (resourceMonitor.Failure is Exception resourceError)
            {
                context.Fail(resourceError);
                throw resourceError;
            }
            context.Cancel();
            throw;
        }
        catch (Exception error)
        {
            resourceMonitor.Dispose();
            context.Fail(error);
            throw;
        }
    }

    private void ExecuteStage(RunContext context, IPipelineStage stage)
    {
        using var span = context.Trace.Begin(stage.Name, PerformanceSpanKind.Stage, PerformanceBackend.Mixed, stage.Name);
        try
        {
            context.PrepareStageResources(stage.Name);
            stage.Execute(context);
            context.ReleaseStageResources();
            span?.Complete();
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            context.SaveTrace();
        }
    }

    private RunData CreateRun(IProjectStore store, string target)
    {
        return new RunData
        {
            StorageVersion = 3,
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
