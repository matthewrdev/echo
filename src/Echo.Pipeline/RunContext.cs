using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Storage;
using Echo.Telemetry;
using System.Diagnostics;

namespace Echo.Pipeline;

public sealed class RunContext : IDisposable
{
    private readonly ICheckpointStore checkpoints;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly Action<object>? output;
    private readonly JsonSerializerOptions eventOptions;
    private readonly object reportGate = new();
    private readonly HashSet<string> processingStages = new(StringComparer.Ordinal);
    private readonly RunProcessingMetadata processing;
    private bool processingDirty = true;
    private long sequence;
    private readonly IBulkArtifactStore bulk;
    private BulkDataSession? data;
    private string? scratchDirectory;
    private long lastResourceReport;
    private WorkBuffers? work;
    private long diskReadBytes;
    private long diskWrittenBytes;
    private long pageEvictions;
    private long cachePeakBytes;
    private readonly ProcessingTelemetryTracker telemetry;

    public RunContext(IProjectStore store,
        RunData run,
        CancellationToken cancellation,
        Action<object>? output,
        ICheckpointStore checkpoints,
        IJsonCodec jsonCodec,
        IFileOperations fileOperations, IBulkArtifactStore bulk, ProcessingResourcePlan resources, ProcessingMemoryTracker memory,
        ProcessingTelemetryTracker telemetry)
    {
        Store = store;
        Run = run;
        Cancellation = cancellation;
        this.output = output;
        this.checkpoints = checkpoints;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.bulk = bulk;
        Resources = resources;
        Memory = memory;
        this.telemetry = telemetry;
        eventOptions = new JsonSerializerOptions(jsonCodec.Options)
        {
            WriteIndented = false
        };
        Images = run.Snapshot.Images
            .Where(image => image.Enabled)
            .OrderBy(image => image.CapturedAt)
            .ThenBy(image => image.Name)
            .ToList();
        var processingPath = store.PathFor($"runs/{run.Id}/processing.json");
        processing = File.Exists(processingPath)
            ? jsonCodec.Read<RunProcessingMetadata>(processingPath)
            : new RunProcessingMetadata { RunId = run.Id, RequestedDepthBackend = run.Snapshot.Settings.DepthBackend };
        var eventPath = store.PathFor($"runs/{run.Id}/events.ndjson");
        sequence = File.Exists(eventPath) ? File.ReadLines(eventPath).LongCount() : 0;
    }

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
        get;
    }
    public long WorkingMemoryBytes => Resources.MemoryBytes - Resources.HeadroomBytes - Resources.CacheBytes - Math.Min(64 * 1024 * 1024, Resources.CacheBytes / 4);
    public bool Paged => Resources.Mode == ProcessingMemoryMode.Paged;
    public BulkDataSession Data => data ??= bulk.OpenSession(Resources.CacheBytes, Resources.DiskBytes == 0 ? 0 : Resources.DiskBytes - Resources.DiskBytes / 4);
    public WorkBuffers? Work => Paged ? work ??= new WorkBuffers(Data, ScratchDirectory,
        Math.Min(64 * 1024 * 1024, Resources.CacheBytes / 4), Resources.DiskBytes / 4) : null;

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
        }
    }

    public void ReleaseStageResources()
    {
        lock (reportGate)
        {
            ReleaseBuffers();
        }
    }

    private void ReleaseBuffers()
    {
        work?.Dispose();
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
        return Paged && bulk.Supports(typeof(T)) ? Data.Read<T>(path) : jsonCodec.Read<T>(path);
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
            ScratchBytes = (paging?.ScratchBytes ?? 0) + (work?.IndexDiskBytes ?? 0)
        };
    }
    public void ReportResources()
    {
        lock (reportGate)
        {
            var usage = MeasureResources();
            if (usage.WorkingSetBytes > Resources.MemoryBytes)
            {
                throw new EchoException($"Processing exceeded its {Resources.MemoryBytes / 1048576:N0} MiB RAM budget (observed {usage.WorkingSetBytes / 1048576:N0} MiB, including registered native workers). Completed checkpoints are retained. Increase ProcessingMemoryBudgetMb and resume; resolution was not changed.", 5);
            }
            if (Run.Progress != null && Environment.TickCount64 - lastResourceReport >= 5000)
            {
                lastResourceReport = Environment.TickCount64;
                Run.Progress.Resources = usage;
                SaveRun();
                Report("resourceProgress", new
                {
                    stage = Run.Progress.Stage,
                    resources = usage
                });
            }
        }
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

    public ReconstructionScene Scene() => ReadArtifact<ReconstructionScene>("alignment", "scene.json");

    public List<string> DepthUnits() => ReadArtifact<List<string>>("depth", "units.json");

    public List<DepthMap> DepthMaps() => DepthUnits().Select(path => ReadData<DepthMap>(Store.PathFor(path + "/depth.json"))).ToList();

    public string Build(string stage, object inputs, Action<string> action, Func<ProcessingMetadata>? metadata = null,
        string? algorithmVersion = null)
    {
        var owner = stage switch
        {
            "feature-image" => "features",
            "features-preview" => "features",
            "match-pair" => "matching",
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
            _ => stage
        };
        if (Run.Snapshot.StageRevisions.TryGetValue(owner, out var resetRevision))
        {
            inputs = new
            {
                resetRevision,
                inputs
            };
        }

        var reused = true;
        if (metadata == null)
        {
            metadata = () => new ProcessingMetadata
            {
                SourceArtifacts = ProcessingArtifactReferences.Find(inputs, jsonCodec.Options),
                Executions = [new ProcessingExecution { Component = stage }]
            };
        }
        var artifact = checkpoints.Build(Store, stage, inputs, directory =>
        {
            reused = false;
            action(directory);
        }, Cancellation, Report, metadata, algorithmVersion);
        lock (reportGate)
        {
            var path = artifact + "/processing.json";
            var manifest = jsonCodec.Read<Artifact>(Store.PathFor(artifact + "/manifest.json"));
            var recorded = manifest.Files.ContainsKey("processing.json");
            processing.Artifacts[artifact] = new ArtifactProcessingReference
            {
                Stage = stage,
                Reused = reused,
                MetadataPath = recorded ? path : null,
                ProvenanceStatus = recorded ? "recorded" : "unknown-legacy"
            };
            processingDirty = true;
            Report("artifactProcessing", new
            {
                stage,
                artifact,
                processing = processing.Artifacts[artifact]
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
            Report("checkpoint", new
            {
                stage,
                artifact
            });
        }
    }

    public void BeginOperation(string stage, string operation)
        => ReportOperation(stage, new OperationProgress(operation, operation + "…"));

    public void ReportOperation(string stage, OperationProgress progress)
    {
        Report("stageProgress", new
        {
            stage,
            operation = progress.Operation,
            message = progress.Message,
            completed = progress.Completed,
            total = progress.Total,
            unit = progress.Unit
        });
    }

    public void ReportProgress(string stage, string message, int? completed = null, int? total = null)
    {
        Report("stageProgress", new
        {
            stage,
            message,
            completed,
            total,
            unit = stage == "matching" ? "image pairs" : stage is "features" or "depth" ? "images" : "items"
        });
    }

    public void Start()
    {
        Run.Status = "running";
        Run.Error = null;
        SaveRun();
        Report("runStarted", new
        {
            Run.Target,
            revision = Run.Snapshot.Revision
        });
    }

    public void Complete()
    {
        Run.Status = "completed";
        SaveRun();
        Report("runCompleted", new
        {
            stage = Run.Target,
            Run.Artifacts
        });
    }

    public void Cancel()
    {
        Run.Status = "cancelled";
        SaveRun();
        Report("runCancelled", new
        {
        });
    }

    public void Fail(Exception error)
    {
        Run.Status = error is EchoException { ExitCode: 5 } ? "needs-input" : "failed";
        Run.Error = error.Message;
        SaveRun();
        Report("runFailed", new
        {
            error = error.Message,
            details = error.ToString()
        });
    }

    public void Report(string type, object payload)
    {
        lock (reportGate)
        {
            UpdateProgress(type, payload);

            var record = new
            {
                schemaVersion = 1,
                sequence = ++sequence,
                timestamp = DateTimeOffset.UtcNow,
                runId = Run.Id,
                type,
                payload
            };
            File.AppendAllText(Store.PathFor($"runs/{Run.Id}/events.ndjson"), JsonSerializer.Serialize(record, eventOptions) + "\n");
            output?.Invoke(record);
        }
    }

    private void UpdateProgress(string type, object payload)
    {
        if (type is "stageStarted" or "stageProgress" or "imageCheckpoint" or "pairCheckpoint")
        {
            var data = JsonSerializer.SerializeToElement(payload, jsonCodec.Options);
            var stage = data.GetProperty("stage").GetString() ?? "";
            var reportedStage = stage;
            if (stage is "cloud-preview" or "features-preview" or "depth-preview")
            {
                return;
            }
            stage = stage switch
            {
                "feature-image" => "features",
                "match-pair" => "matching",
                "alignment-preview" => "alignment",
                "alignment-initial" => "alignment",
                "alignment-guided-pair" => "alignment",
                "semantic-image" => "depth",
                "semantics" => "depth",
                "cliff-depth-image" => "depth",
                "fusion-chunk" => "fusion",
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
            var completed = data.TryGetProperty("completed", out var done) && done.ValueKind == JsonValueKind.Number
                ? (long?)done.GetInt64()
                : null;
            var total = data.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number
                ? (long?)count.GetInt64()
                : null;
            var unit = data.TryGetProperty("unit", out var workUnit) ? workUnit.GetString() ?? "items"
                : stage == "matching" ? "image pairs" : "images";
            var operation = data.TryGetProperty("operation", out var activity) ? activity.GetString() ?? "" : previous?.Operation ?? "";
            var started = operation == previous?.Operation ? previous.OperationStarted : DateTimeOffset.UtcNow;
            Run.Progress = new RunProgress
            {
                Resources = MeasureResources(),
                Operation = operation,
                OperationStarted = started,
                Unit = unit,
                Stage = stage,
                Phase = checking ? "checking" : "processing",
                Completed = completed,
                Total = total,
                Message = data.TryGetProperty("message", out var message) ? message.GetString() ?? "" : completed.HasValue
                    ? checking ? $"Reusing saved {stage}: {completed:N0} of {total:N0} {unit} checked."
                        : $"{completed:N0} / {total:N0} {unit} completed"
                    : "Processing saved inputs…"
            };
            SaveRun();
        }
    }

    private void SaveRun()
    {
        Run.ProcessingMetadataFile = "processing.json";
        if (processingDirty)
        {
            fileOperations.AtomicJson(Store.PathFor($"runs/{Run.Id}/processing.json"), processing);
            processingDirty = false;
        }
        fileOperations.AtomicJson(Store.PathFor($"runs/{Run.Id}/run.json"), Run);
    }
}
