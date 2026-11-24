using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Echo.Pipeline;

public sealed class Processor : IProcessor
{
    private readonly IReadOnlyList<IPipelineStage> stages;
    private readonly IRunContextFactory contextFactory;
    private readonly IFileOperations fileOperations;
    private readonly ILogger<Processor> logger;

    public Processor(IEnumerable<IPipelineStage> stages,
        IRunContextFactory contextFactory,
        IFileOperations fileOperations,
        ILogger<Processor>? logger = null)
    {
        this.stages = stages.OrderBy(stage => stage.Order).ToArray();
        this.contextFactory = contextFactory;
        this.fileOperations = fileOperations;
        this.logger = logger ?? NullLogger<Processor>.Instance;
    }

    public IReadOnlyList<string> Stages => stages.Select(stage => stage.Name).ToArray();

    public RunData Execute(IProjectStore store,
        string target,
        CancellationToken token,
        string? resumeId = null,
        Action<object>? output = null,
        TextureRunOptions? textureOptions = null,
        SubjectDistanceRunOptions? subjectDistanceOptions = null,
        bool headless = false)
    {
        if (resumeId != null && textureOptions != null)
        {
            throw new EchoException("Resume preserves its saved texture settings. Use run start to change colour tuning or edge softening.", 2);
        }

        if (resumeId != null && subjectDistanceOptions != null)
        {
            throw new EchoException("Resume preserves its saved subject distance settings. Use run start to change --subject-distance.", 2);
        }

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

        textureOptions?.Apply(run.Snapshot.Settings);
        subjectDistanceOptions?.Apply(run.Snapshot.Settings);
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
        using var context = contextFactory.Create(store, run, cancellation.Token, output, headless);
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
            logger.LogError(error, "Processing run {RunId} failed", run.Id);
            throw;
        }
    }

    private void ExecuteStage(RunContext context, IPipelineStage stage)
    {
        using var span = context.Trace.Begin(stage.Name, PerformanceSpanKind.Stage, PerformanceBackend.Mixed, stage.Name);
        try
        {
            logger.LogDebug("Starting processing stage {Stage}", stage.Name);
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
