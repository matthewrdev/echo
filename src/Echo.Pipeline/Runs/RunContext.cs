using System.Diagnostics;
using Echo.Compute;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Artifacts;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Storage.Journals;
using Echo.Storage.Resources;
using Echo.Storage;
using Echo.Telemetry;

namespace Echo.Pipeline.Runs;

public sealed class RunContext : IDisposable
{
    private readonly ICheckpointStore checkpoints;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly Action<object>? output;
    private readonly RunEventRecordCodec eventCodec = new();
    private readonly RunProgressCodec progressCodec = new();
    private readonly object reportGate = new();
    private readonly object traceSaveGate = new();
    private readonly OperationProgressFilter progressFilter = new();
    private readonly Stopwatch progressClock = Stopwatch.StartNew();
    private readonly HashSet<string> processingStages = new(StringComparer.Ordinal);
    private readonly RunProcessingJournal processing;
    private long sequence;
    private readonly IBulkArtifactStore bulk;
    private readonly IProcessingResourcePlanner resourcePlanner;
    private readonly ProcessingResourcePlan runResources;
    private BulkDataSession? data;
    private string? scratchDirectory;
    private long lastResourceReport;
    private long lastProgressSave = long.MinValue;
    private long lastEventFlush = long.MinValue;
    private bool eventsPending;
    private BinaryJournal<RunEvent>? eventWriter;
    private WorkBuffers? work;
    private long diskReadBytes;
    private long diskWrittenBytes;
    private long pageEvictions;
    private long cachePeakBytes;
    private readonly ProcessingTelemetryTracker telemetry;
    private readonly IComputeService? compute;
    private readonly IPerformanceTraceStore traces;
    private PerformanceTraceSession? traceSession;
    private bool traceWriteWarning;
    public IPerformanceTrace Trace
    {
        get;
    }

    public RunContext(IProjectStore store,
        RunData run,
        CancellationToken cancellation,
        Action<object>? output,
        ICheckpointStore checkpoints,
        IJsonCodec jsonCodec,
        IFileOperations fileOperations, IBulkArtifactStore bulk, IProcessingResourcePlanner resourcePlanner, ProcessingResourcePlan resources, ProcessingMemoryTracker memory,
        ProcessingTelemetryTracker telemetry, IPerformanceTrace trace, IPerformanceTraceStore traces, IComputeService? compute = null, bool headless = false)
    {
        this.compute = compute;
        Headless = headless;
        Trace = trace;
        this.traces = traces;
        Store = store;
        Run = run;
        Cancellation = cancellation;
        this.output = output;
        this.checkpoints = checkpoints;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.bulk = bulk;
        this.resourcePlanner = resourcePlanner;
        runResources = resources;
        Resources = resources;
        Memory = memory;
        this.telemetry = telemetry;
        Images = run.Snapshot.Images
            .Where(image => image.Enabled)
            .OrderBy(image => image.CapturedAt)
            .ThenBy(image => image.Name)
            .ToList();
        if (run.ProcessingMetadataFile == "processing.json")
        {
            throw new EchoException($"Run {run.Id} uses legacy provenance. Run Echo.Migrate --run-provenance-only for this project.", 2);
        }

        processing = new RunProcessingJournal(store.PathFor($"runs/{run.Id}"));
        var eventReader = new BinaryJournalReader<RunEvent>(eventCodec);
        using var events = eventReader.OpenRead(store.PathFor($"runs/{run.Id}/events.bin"));
        if (events != null)
        {
            sequence = eventReader.ReadLast(events)?.Sequence ?? 0;
        }
    }

    public string ComputeImplementation(ComputeOperation operation) =>
        Run.Snapshot.Settings.ComputeBackend == ComputeBackend.Cpu ? "cpu" : compute?.GetImplementation(operation) ?? "cpu";

    public IProjectStore Store
    {
        get;
    }

    public ProcessingMemoryTracker Memory
    {
        get;
    }
    public ProcessingResourcePlan Resources
    {
        get; private set;
    }
    public long WorkingMemoryBytes => Resources.MemoryBytes - Resources.HeadroomBytes - Resources.CacheBytes;
    public bool Paged => Resources.Mode == ProcessingMemoryMode.Paged;
    public BulkDataSession Data => data ??= bulk.OpenSession(Resources.CacheBytes, Resources.DiskBytes);
    public WorkBuffers? Work => Paged ? work ??= new WorkBuffers(Data, ScratchDirectory) : null;

    public string ScratchDirectory
    {
        get
        {
            if (scratchDirectory == null)
            {
                scratchDirectory = Store.PathFor($"work/paging-{Run.Id}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(scratchDirectory);
            }

            return scratchDirectory;
        }
    }

    public void Dispose()
    {
        lock (reportGate)
        {
            ReleaseStageResources();
            Memory.Dispose();
            traceSession?.Dispose();
            eventWriter?.Dispose();
            processing.Dispose();
        }
    }

    public void ReleaseStageResources()
    {
        lock (reportGate)
        {
            ReleaseBuffers();
        }
    }

    public void PrepareStageResources(string stage)
    {
        lock (reportGate)
        {
            ReleaseBuffers();
            Resources = resourcePlanner.PlanStage(runResources, stage);
        }
    }

    private void ReleaseBuffers()
    {
        work = null;
        if (data != null)
        {
            data.Dispose();
            var final = data.Statistics;
            diskReadBytes += final.ReadBytes;
            diskWrittenBytes += final.WrittenBytes;
            pageEvictions += final.Evictions;
            cachePeakBytes = Math.Max(cachePeakBytes, final.PeakResidentBytes);
            data = null;
        }

        if (scratchDirectory != null)
        {
            Directory.Delete(scratchDirectory, true);
            scratchDirectory = null;
        }
    }

    public T ReadData<T>(string path)
    {
        Cancellation.ThrowIfCancellationRequested();
        return Paged && bulk.SupportsPaging(typeof(T)) ? Data.Read<T>(path) : jsonCodec.Read<T>(path);
    }

    public IEnumerable<VolumeSample> ReadArtifactChunk(string artifact)
        => jsonCodec.Read<FusedVolume>(Store.PathFor(artifact + "/volume.json")).Samples;

    public ResourceProgress MeasureResources()
    {
        var usage = Memory.Sample();
        var paging = data?.Statistics;
        return new ResourceProgress
        {
            Processor = telemetry.Sample(Memory.ProcessIds()),
            WorkingSetBytes = usage.ResidentBytes,
            PeakWorkingSetBytes = usage.PeakBytes,
            ManagedBytes = usage.ManagedBytes,
            MemoryBudgetBytes = Resources.MemoryBytes,
            CacheResidentBytes = paging?.ResidentBytes ?? 0,
            CachePeakBytes = Math.Max(cachePeakBytes, paging?.PeakResidentBytes ?? 0),
            DiskReadBytes = diskReadBytes + (paging?.ReadBytes ?? 0),
            DiskWrittenBytes = diskWrittenBytes + (paging?.WrittenBytes ?? 0),
            PageEvictions = pageEvictions + (paging?.Evictions ?? 0),
            ScratchBytes = paging?.ScratchBytes ?? 0
        };
    }
    public void ReportResources()
    {
        lock (reportGate)
        {
            FlushEventHistory(force: false);
            var usage = MeasureResources();
            if (usage.WorkingSetBytes > Resources.MemoryBytes)
            {
                throw new EchoException($"Processing exceeded its {Resources.MemoryBytes / 1048576:N0} MiB RAM budget (observed {usage.WorkingSetBytes / 1048576:N0} MiB, including registered native workers). Completed checkpoints are retained. Increase ProcessingMemoryBudgetMb and resume; resolution was not changed.", 5);
            }

            if (Run.Progress != null && Environment.TickCount64 - lastResourceReport >= 5000)
            {
                lastResourceReport = Environment.TickCount64;
                SaveTrace();
                Run.Progress.Resources = usage;
                SaveProgress(true);
                Report("resourceProgress", new Dictionary<string, object?>
                {
                    ["stage"] = Run.Progress.Stage,
                    ["resources"] = RunEventValues.Resources(usage)
                });
            }
        }
    }

    public bool Headless
    {
        get;
    }

    public RunData Run
    {
        get;
    }
    public CancellationToken Cancellation
    {
        get;
    }
    public IReadOnlyList<Photo> Images
    {
        get;
    }

    public string Artifact(string stage) => Run.Artifacts[stage];

    public T ReadArtifact<T>(string stage, string file) => jsonCodec.Read<T>(Store.PathFor(Artifact(stage) + "/" + file));

    public ReconstructionScene Scene() => ReadData<ReconstructionScene>(Store.PathFor(Artifact("alignment") + "/scene.json"));

    public List<string> DepthUnits() => ReadArtifact<List<string>>("depth", "units.json");

    public List<DepthMap> DepthMaps() => DepthUnits().Select(path => ReadData<DepthMap>(Store.PathFor(path + "/depth.json"))).ToList();

    private ProcessingExecution DescribeCompute(string component, ComputeScope? scope)
    {
        var devices = scope?.Devices ?? [];
        var accelerated = devices.Length > 0;
        return new ProcessingExecution
        {
            Component = component,
            RequestedBackend = Run.Snapshot.Settings.ComputeBackend.ToString().ToLowerInvariant(),
            ActualBackend = accelerated ? "cpu+metal" : "cpu",
            Device = accelerated ? string.Join("; ", devices) : null,
            Implementation = accelerated ? string.Join("; ", scope!.Executions.Select(execution => execution.Implementation).Distinct(StringComparer.Ordinal)) : "Echo managed processing",
            Precision = accelerated ? "see operation executions; stage-defined CPU" : "stage-defined"
        };
    }

    private List<ProcessingExecution> DescribeExecutions(string component, ComputeScope? scope)
    {
        var result = new List<ProcessingExecution> { DescribeCompute(component, scope) };
        foreach (var execution in scope?.Executions ?? [])
        {
            result.Add(new ProcessingExecution
            {
                Component = component + "." + execution.Operation.ToString().ToLowerInvariant(),
                RequestedBackend = Run.Snapshot.Settings.ComputeBackend.ToString().ToLowerInvariant(),
                ActualBackend = execution.Backend.ToString().ToLowerInvariant(),
                Device = execution.Device,
                Implementation = execution.Implementation,
                Precision = execution.Precision == ComputePrecision.IntegerExact ? "integer-exact" : "float32"
            });
        }

        return result;
    }

    public string Build(string stage, object inputs, Action<string> action, Func<ProcessingMetadata>? metadata = null,
        string? algorithmVersion = null, IReadOnlyList<object>? compatibleInputs = null, IEnumerable<string?>? sources = null)
    {
        var owner = stage switch
        {
            "feature-image" => "features",
            "features-preview" => "features",
            "match-pair" => "matching",
            "matching-feature-image" => "matching",
            "matching-preview" => "matching",
            "semantic-image" => "depth",
            "semantics" => "depth",
            "cliff-depth-image" => "depth",
            "depth-image" => "depth",
            "depth-preview" => "depth",
            "alignment-preview" => "alignment",
            "alignment-initial" => "alignment",
            "alignment-guided-pair" => "alignment",
            "cloud-preview" => "cloud",
            "fusion-chunk" => "fusion",
            "fusion-grid" => "fusion",
            "texture-layout" => "texture",
            _ => stage
        };
        if (Run.Snapshot.StageRevisions.TryGetValue(owner, out var resetRevision))
        {
            inputs = new Dictionary<string, object?>
            {
                ["resetRevision"] = resetRevision,
                ["inputs"] = inputs
            };
            compatibleInputs = compatibleInputs?.Select(compatible => (object)new Dictionary<string, object?>
            {
                ["resetRevision"] = resetRevision,
                ["inputs"] = compatible
            }).ToArray();
        }

        using var performance = Trace.Begin(stage, PerformanceSpanKind.Checkpoint, PerformanceBackend.Mixed, owner);
        using var computeScope = compute?.BeginScope(Math.Max(4, WorkingMemoryBytes / 4));
        var reused = true;
        if (metadata == null)
        {
            metadata = () => new ProcessingMetadata
            {
                SourceArtifacts = sources?.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList() ?? [],
                Executions = DescribeExecutions(stage, computeScope)
            };
        }
        else
        {
            var sourceMetadata = metadata;
            metadata = () =>
            {
                var result = sourceMetadata();
                if (computeScope?.Devices.Length > 0)
                {
                    result.Executions.AddRange(DescribeExecutions(stage + ".compute", computeScope));
                }

                return result;
            };
        }

        string artifact;
        bool recorded;
        try
        {
            artifact = checkpoints.Build(Store, stage, inputs, directory =>
            {
                reused = false;
                action(directory);
            }, Cancellation, Report, out recorded, metadata, algorithmVersion, compatibleInputs);
            performance?.Complete(reused ? PerformanceStatus.Reused : PerformanceStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            performance?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            performance?.Complete(PerformanceStatus.Failed);
            throw;
        }

        lock (reportGate)
        {
            var path = artifact + "/processing.json";
            var reference = new ArtifactProcessingReference
            {
                Stage = stage,
                Reused = reused,
                MetadataPath = recorded ? path : null,
                ProvenanceStatus = recorded ? "recorded" : "unknown-legacy"
            };
            processing.Append(artifact, reference);
            Report("artifactProcessing", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["artifact"] = artifact,
                ["processing"] = RunEventValues.Processing(reference)
            });
        }

        return artifact;
    }

    public void Publish(string stage, string artifact)
    {
        lock (reportGate)
        {
            Run.Artifacts[stage] = artifact;
            SaveRun();
            Report("checkpoint", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["artifact"] = artifact
            });
        }
    }

    public void PublishPreview(string stage, string artifact)
    {
        lock (reportGate)
        {
            if (Run.Artifacts.GetValueOrDefault(stage) != artifact)
            {
                Publish(stage, artifact);
                return;
            }

            Report("checkpoint", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["artifact"] = artifact
            });
        }
    }

    public void BeginOperation(string stage, string operation)
        => ReportOperation(stage, new OperationProgress(operation, operation + "…"));

    public void ReportOperation(string stage, OperationProgress progress)
    {
        lock (reportGate)
        {
            if (!progressFilter.Accept(stage, progress, progressClock.Elapsed))
            {
                return;
            }

            Trace.Operation(progress);
            Report("stageProgress", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["operation"] = progress.Operation,
                ["steps"] = progress.Steps,
                ["stepIndex"] = progress.StepIndex,
                ["stageSteps"] = progress.StageSteps,
                ["stageStepIndex"] = progress.StageStepIndex,
                ["message"] = progress.Message,
                ["completed"] = progress.Completed,
                ["total"] = progress.Total,
                ["unit"] = progress.Unit
            });
        }
    }

    public void ReportProgress(string stage, string message, int? completed = null, int? total = null, string? unit = null)
    {
        lock (reportGate)
        {
            if (completed.HasValue && !progressFilter.Accept(stage, new OperationProgress("", message, completed, total), progressClock.Elapsed))
            {
                return;
            }

            Report("stageProgress", new Dictionary<string, object?>
            {
                ["stage"] = stage,
                ["message"] = message,
                ["completed"] = completed,
                ["total"] = total,
                ["unit"] = unit ?? (stage == "matching" ? "image pairs" : stage is "features" or "depth" ? "images" : "items")
            });
        }
    }

    public void BeginStep(string stage, string[] steps, int stepIndex, int? total = null)
    {
        ReportOperation(stage, new OperationProgress(steps[stepIndex],
            $"{steps[stepIndex]} · step {stepIndex + 1} of {steps.Length}", 0, total,
            stepIndex == steps.Length - 1 ? "results" : "images")
        {
            Steps = steps,
            StepIndex = stepIndex
        });
    }

    public void Start()
    {
        traceSession = Trace.BeginRun(Run.Id, Run.Target);
        SaveTrace();
        Run.Status = "running";
        Run.Error = null;
        SaveRun();
        Report("runStarted", new Dictionary<string, object?>
        {
            ["target"] = Run.Target,
            ["revision"] = Run.Snapshot.Revision
        });
    }

    public void Complete()
    {
        traceSession?.Finish(PerformanceStatus.Completed);
        SaveTrace();
        Run.Status = "completed";
        SaveRun();
        Report("runCompleted", new Dictionary<string, object?>
        {
            ["stage"] = Run.Target,
            ["artifacts"] = Run.Artifacts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value)
        });
    }

    public void Cancel()
    {
        traceSession?.Finish(PerformanceStatus.Cancelled);
        SaveTrace();
        Run.Status = "cancelled";
        SaveRun();
        Report("runCancelled", new Dictionary<string, object?>
        {
        });
    }

    public void Fail(Exception error)
    {
        traceSession?.Finish(PerformanceStatus.Failed);
        SaveTrace();
        Run.Status = error is EchoException { ExitCode: 5 } ? "needs-input" : "failed";
        Run.Error = error.Message;
        SaveRun();
        Report("runFailed", new Dictionary<string, object?>
        {
            ["error"] = error.Message,
            ["details"] = error.ToString()
        });
    }

    public void Report(string type, Dictionary<string, object?> payload)
    {
        lock (reportGate)
        {
            UpdateProgress(type, payload);

            var record = new RunEvent(++sequence, DateTimeOffset.UtcNow, Run.Id, type, payload);
            eventWriter ??= new BinaryJournal<RunEvent>(Store.PathFor($"runs/{Run.Id}/events.bin"), eventCodec);
            eventWriter.Append(record, durable: false);
            eventsPending = true;
            FlushEventHistory(type is not ("stageProgress" or "resourceProgress"));
            output?.Invoke(record);
        }
    }

    private void FlushEventHistory(bool force)
    {
        var now = Environment.TickCount64;
        var due = lastEventFlush == long.MinValue || now - lastEventFlush >= 250;
        if (eventsPending && (force || due))
        {
            eventWriter!.Flush(durable: false);
            lastEventFlush = now;
            eventsPending = false;
        }
    }

    private void UpdateProgress(string type, Dictionary<string, object?> payload)
    {
        if (type is "stageStarted" or "stageProgress" or "imageCheckpoint" or "pairCheckpoint")
        {
            var stage = payload.GetValueOrDefault("stage") as string ?? "";
            var reportedStage = stage;
            if (stage is "cloud-preview" or "features-preview" or "matching-preview" or "depth-preview")
            {
                return;
            }

            stage = stage switch
            {
                "feature-image" => "features",
                "match-pair" => "matching",
                "matching-feature-image" => "matching",
                "alignment-preview" => "alignment",
                "alignment-initial" => "alignment",
                "alignment-guided-pair" => "alignment",
                "semantic-image" or "semantics" => Run.Progress?.Stage == "matching" ? "matching" : "depth",
                "cliff-depth-image" => "depth",
                "fusion-chunk" => "fusion",
                "fusion-grid" => "fusion",
                "texture-layout" => "texture",
                "depth-image" => "depth",
                _ => stage
            };
            if (type == "stageStarted")
            {
                processingStages.Add(stage);
            }

            var checking = !processingStages.Contains(stage);
            var previous = Run.Progress?.Stage == stage ? Run.Progress : null;
            var beginsChildCheckpoint = type == "stageStarted" && reportedStage != stage;
            if (beginsChildCheckpoint && previous?.Phase == "processing")
            {
                // Checkpoint plumbing must not erase the algorithm's useful
                // operation/counters or pretend another work item has finished.
                return;
            }

            var completed = Count("completed");
            var total = Count("total");
            var unit = payload.GetValueOrDefault("unit") as string ?? (stage == "matching" ? "image pairs" : "images");
            var operation = payload.GetValueOrDefault("operation") as string ?? previous?.Operation ?? "";
            var operationChanged = operation != previous?.Operation;
            var stageSteps = payload.GetValueOrDefault("stageSteps") as string[] ?? previous?.StageSteps ?? [];
            var stageStepIndex = payload.GetValueOrDefault("stageStepIndex") as int? ?? previous?.StageStepIndex;
            var steps = payload.GetValueOrDefault("steps") as string[] ?? (operationChanged ? [] : previous?.Steps ?? []);
            var reportedStep = payload.GetValueOrDefault("stepIndex") as int?;
            var stepIndex = reportedStep ?? (operationChanged ? null : previous?.StepIndex);
            var continuesStep = steps.Length > 0 && reportedStep == null;
            var uncounted = !completed.HasValue && !total.HasValue;
            var messageOnly = type == "stageProgress" && !payload.ContainsKey("operation") && uncounted;
            if (continuesStep || messageOnly)
            {
                completed ??= previous?.Completed;
                total ??= previous?.Total;
                if (messageOnly)
                {
                    unit = previous?.Unit ?? unit;
                }
            }

            var started = operation == previous?.Operation ? previous.OperationStarted : DateTimeOffset.UtcNow;
            Run.Progress = new RunProgress
            {
                Steps = steps,
                StepIndex = stepIndex,
                StageSteps = stageSteps,
                StageStepIndex = stageStepIndex,
                Resources = previous?.Resources ?? MeasureResources(),
                Operation = operation,
                OperationStarted = started,
                Unit = unit,
                Stage = stage,
                Phase = checking ? "checking" : "processing",
                Completed = completed,
                Total = total,
                Message = payload.GetValueOrDefault("message") as string ?? (completed.HasValue
                    ? checking ? $"Reusing saved {stage}: {completed:N0} of {total:N0} {unit} checked."
                        : $"{completed:N0} / {total:N0} {unit} completed"
                    : "Processing saved inputs…")
            };
            var completedOperation = total.HasValue && completed >= total;
            SaveProgress(operationChanged || completedOperation || previous == null);
        }

        long? Count(string key) => payload.GetValueOrDefault(key) switch
        {
            int value => value,
            long value => value,
            _ => null
        };
    }

    private void SaveProgress(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && lastProgressSave != long.MinValue && now - lastProgressSave < 1000)
        {
            return;
        }
        // The event journal is the incremental history. Polling only needs this
        // small latest-value snapshot, not another copy of every source photo.
        var progress = Run.Progress!;
        fileOperations.AtomicWrite(Store.PathFor($"runs/{Run.Id}/progress.bin"), stream => progressCodec.Write(stream, progress));
        lastProgressSave = now;
    }

    public void SaveTrace()
    {
        if (traceSession == null)
        {
            return;
        }

        try
        {
            lock (traceSaveGate)
            {
                var checkpoint = traceSession.CaptureCheckpoint();
                traces.Save(Store.PathFor($"runs/{Run.Id}"), checkpoint.Document);
                traceSession.Acknowledge(checkpoint);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!traceWriteWarning)
            {
                traceWriteWarning = true;
                Report("warning", new Dictionary<string, object?>
                {
                    ["message"] = "Performance trace could not be saved: " + error.Message
                });
            }
        }
    }

    private void SaveRun()
    {
        processing.Flush();
        Run.ProcessingMetadataFile = processing.Exists ? RunProcessingJournal.FileName : null;
        Store.SaveRun(Run);
        File.Delete(Store.PathFor($"runs/{Run.Id}/progress.bin"));
        lastProgressSave = long.MinValue;
    }
}
