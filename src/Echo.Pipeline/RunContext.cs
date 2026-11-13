using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;

namespace Echo.Pipeline;

public sealed class RunContext
{
    private readonly ICheckpointStore checkpoints;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly Action<object>? output;
    private readonly JsonSerializerOptions eventOptions;
    private readonly object reportGate = new();
    private readonly HashSet<string> processingStages = new(StringComparer.Ordinal);
    private long sequence;

    public RunContext(IProjectStore store,
        RunData run,
        CancellationToken cancellation,
        Action<object>? output,
        ICheckpointStore checkpoints,
        IJsonCodec jsonCodec,
        IFileOperations fileOperations)
    {
        Store = store;
        Run = run;
        Cancellation = cancellation;
        this.output = output;
        this.checkpoints = checkpoints;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        eventOptions = new JsonSerializerOptions(jsonCodec.Options)
        {
            WriteIndented = false
        };
        Images = run.Snapshot.Images
            .Where(image => image.Enabled)
            .OrderBy(image => image.CapturedAt)
            .ThenBy(image => image.Name)
            .ToList();
        var eventPath = store.PathFor($"runs/{run.Id}/events.ndjson");
        sequence = File.Exists(eventPath) ? File.ReadLines(eventPath).LongCount() : 0;
    }

    public IProjectStore Store
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

    public ReconstructionScene Scene() => ReadArtifact<ReconstructionScene>("alignment", "scene.json");

    public List<string> DepthUnits() => ReadArtifact<List<string>>("depth", "units.json");

    public List<DepthMap> DepthMaps() => DepthUnits().Select(path => jsonCodec.Read<DepthMap>(Store.PathFor(path + "/depth.json"))).ToList();

    public string Build(string stage, object inputs, Action<string> action)
    {
        var owner = stage switch
        {
            "feature-image" => "features",
            "features-preview" => "features",
            "match-pair" => "matching",
            "depth-image" => "depth",
            "depth-preview" => "depth",
            "alignment-preview" => "alignment",
            "alignment-initial" => "alignment",
            "alignment-guided-pair" => "alignment",
            "cloud-preview" => "cloud",
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

        return checkpoints.Build(Store, stage, inputs, action, Cancellation, Report);
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

    public void ReportProgress(string stage, string message, int? completed = null, int? total = null)
    {
        Report("stageProgress", new
        {
            stage,
            message,
            completed,
            total
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
                "depth-image" => "depth",
                _ => stage
            };
            if (type == "stageStarted")
            {
                processingStages.Add(stage);
            }
            var checking = !processingStages.Contains(stage);
            var previous = Run.Progress?.Stage == stage ? Run.Progress : null;
            var completed = data.TryGetProperty("completed", out var done) && done.ValueKind == JsonValueKind.Number
                ? done.GetInt32()
                : previous?.Completed;
            var total = data.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number
                ? count.GetInt32()
                : previous?.Total;
            var unit = stage == "matching" ? "image pairs" : "images";
            Run.Progress = new RunProgress
            {
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
        fileOperations.AtomicJson(Store.PathFor($"runs/{Run.Id}/run.json"), Run);
    }
}
