using System.Text;
using System.Text.Json;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Alignment;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Storage.Journals;
using Echo.Telemetry;

namespace Echo.Migrate;

public sealed class PackedStorageMigrator
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IPerformanceTraceStore traces;
    private readonly IAlignmentPreviewWriter alignmentPreviews;
    private readonly ImagePreviewRecordCodec units = new();
    private readonly RunEventRecordCodec events = new();
    private readonly DenseCloudReportCodec cloud = new();
    private readonly FusionPreviewCodec fusion = new();
    private readonly ProjectSnapshotCodec snapshots = new();
    private readonly RunProgressCodec progressSnapshots = new();

    public PackedStorageMigrator(IJsonCodec json, IFileOperations files, IPerformanceTraceStore traces,
        IAlignmentPreviewWriter alignmentPreviews)
    {
        this.json = json;
        this.files = files;
        this.traces = traces;
        this.alignmentPreviews = alignmentPreviews;
    }

    public void MigrateProject(IProjectStore store)
    {
        MigrateSnapshot(store.PathFor("project.json"), store.PathFor("project.bin"));
        var revisions = store.PathFor("revisions");
        if (Directory.Exists(revisions))
        {
            foreach (var source in Directory.EnumerateFiles(revisions, "*.json"))
            {
                MigrateSnapshot(source, Path.ChangeExtension(source, ".bin"));
            }
        }
    }

    public RunData ReadRun(IProjectStore store, string path, out bool legacy)
    {
        using var input = File.OpenRead(path);
        using var document = JsonDocument.Parse(input);
        legacy = document.RootElement.TryGetProperty("snapshot", out _);
        var id = Path.GetFileName(Path.GetDirectoryName(path));
        if (!legacy)
        {
            return store.GetRun(id!);
        }

        return document.RootElement.Deserialize(CoreJsonReadContext.Default.RunData)
            ?? throw new InvalidDataException("Missing legacy run.");
    }

    private void MigrateSnapshot(string source, string target)
    {
        if (!File.Exists(source))
        {
            return;
        }

        var project = json.Read<ProjectData>(source);
        files.AtomicWrite(target, stream => snapshots.Write(stream, project));
        if (json.Encode(project) != json.Encode(snapshots.Read(target)))
        {
            throw new InvalidDataException($"Packed project values differ from {source}.");
        }

        File.Delete(source);
    }

    public int MigrateAlignmentPreviews(IProjectStore store)
    {
        var directories = new List<string>
        {
            store.PathFor("artifacts/alignment"), store.PathFor("artifacts/alignment-preview")
        };
        var runs = store.PathFor("runs");
        if (Directory.Exists(runs))
        {
            directories.AddRange(Directory.EnumerateDirectories(runs).Select(run => Path.Combine(run, "previews/alignment")));
        }

        var count = 0;
        foreach (var folder in directories.Where(Directory.Exists))
        {
            foreach (var source in Directory.EnumerateFiles(folder, "preview.json", SearchOption.AllDirectories))
            {
                var preview = ReadLegacy(source, CoreJsonReadContext.Default.AlignmentPreview);
                var directory = Path.GetDirectoryName(source)!;
                var target = Path.Combine(directory, "preview.bin");
                alignmentPreviews.Write(target, preview, CancellationToken.None);
                var checkedPreview = new AlignmentPreviewReader().Read(target, summary: true);
                if (checkedPreview.TotalPoints != preview.TotalPoints)
                {
                    throw new InvalidDataException("Migrated alignment preview differs from legacy data.");
                }

                ReplaceManifestEntry(directory, "preview.json", "preview.bin");
                File.Delete(source);
                count++;
            }
        }

        return count;
    }

    public void MigrateCloudArtifacts(IProjectStore store)
    {
        foreach (var stage in new[] { "cloud", "cloud-preview" })
        {
            var root = store.PathFor("artifacts/" + stage);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var source = Path.Combine(directory, "cloud.json");
                if (!File.Exists(source))
                {
                    continue;
                }

                var report = ReadCloud(source);
                var target = Path.Combine(directory, "cloud.bin");
                Write(target, writer => cloud.Write(writer, report));
                ReplaceManifestEntry(directory, "cloud.json", "cloud.bin");
                File.Delete(source);
            }
        }
    }

    public void MigrateFaceFlags(IProjectStore store)
    {
        var root = store.PathFor("artifacts/mesh");
        if (!Directory.Exists(root))
        {
            return;
        }

        var codec = new FaceFlagsCodec();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var source = Path.Combine(directory, "vegetation.json");
            if (!File.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(directory, "vegetation.bin");
            var values = json.Read<bool[]>(source);
            files.AtomicWrite(target, stream => codec.Write(stream, values));
            if (!values.AsSpan().SequenceEqual(codec.Read(target)))
            {
                throw new InvalidDataException("Migrated face flags differ from legacy data.");
            }

            ReplaceManifestEntry(directory, "vegetation.json", "vegetation.bin");
            File.Delete(source);
        }
    }

    public bool MigrateRun(IProjectStore store, RunData run)
    {
        var directory = store.PathFor($"runs/{run.Id}");
        var legacyProgress = Path.Combine(directory, "progress.json");
        if (File.Exists(legacyProgress))
        {
            var progress = json.Read<RunProgress>(legacyProgress);
            files.AtomicWrite(Path.Combine(directory, "progress.bin"), stream => progressSnapshots.Write(stream, progress));
            File.Delete(legacyProgress);
        }

        MigrateEvents(directory);
        MigrateTraces(directory);
        var changed = false;
        foreach (var stage in new[] { "features", "depth" })
        {
            var key = stage + "Preview";
            if (!run.Artifacts.TryGetValue(key, out var current))
            {
                continue;
            }

            var source = store.PathFor(current + "/units.json");
            if (!File.Exists(source))
            {
                continue;
            }

            var preview = $"runs/{run.Id}/previews/{stage}";
            var records = json.Read<Dictionary<string, string>>(source).Select(pair => new ImagePreviewRecord(pair.Key, pair.Value));
            MigrateJournal(store.PathFor(preview + "/units.bin"), units, records);
            run.Artifacts[key] = preview;
            changed = true;
        }

        if (run.Artifacts.TryGetValue("fusionPreview", out var fusionPath))
        {
            var folder = store.PathFor(fusionPath);
            var source = Path.Combine(folder, "preview.json");
            if (File.Exists(source))
            {
                var preview = ReadLegacy(source, CoreJsonReadContext.Default.FusionPreview);
                Write(Path.Combine(folder, "preview.bin"), writer => fusion.Write(writer, preview));
                ReplaceManifestEntry(folder, "preview.json", "preview.bin");
                File.Delete(source);
            }
        }

        return changed;
    }

    private void MigrateEvents(string runDirectory)
    {
        var source = Path.Combine(runDirectory, "events.ndjson");
        if (!File.Exists(source))
        {
            return;
        }

        MigrateJournal(Path.Combine(runDirectory, "events.bin"), events,
            File.ReadLines(source).Where(line => !string.IsNullOrWhiteSpace(line)).Select(ReadEvent));
        File.Delete(source);
    }

    private RunEvent ReadEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        var record = document.RootElement;
        var payload = ReadValue(record.GetProperty("payload")) as Dictionary<string, object?>
            ?? throw new InvalidDataException("Run event has no object payload.");
        return new RunEvent(record.GetProperty("sequence").GetInt64(), record.GetProperty("timestamp").GetDateTimeOffset(),
            record.GetProperty("runId").GetString() ?? "", record.GetProperty("type").GetString() ?? "", payload);
    }

    private object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetUInt64(out var unsigned) => unsigned,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => value.EnumerateArray().Select(ReadValue).ToArray(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(item => item.Name, item => ReadValue(item.Value)),
        _ => throw new InvalidDataException("Unsupported run event value.")
    };

    private void MigrateTraces(string runDirectory)
    {
        var directory = Path.Combine(runDirectory, "traces");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (Path.GetFileName(source) == "index.json")
            {
                continue;
            }

            var header = json.Read<PerformanceTraceHeader>(source);
            var legacySpans = Path.ChangeExtension(source, ".spans.ndjson");
            var target = Path.Combine(directory, header.Summary.Id + ".trace.bin");
            if (!File.Exists(target))
            {
                var trace = header.SchemaVersion == 1
                    ? json.Read<PerformanceTraceDocument>(source)
                    : ReadLegacyTrace(header, legacySpans);
                traces.Save(runDirectory, trace);
            }

            var migrated = traces.Read(runDirectory, header.Summary.Id);
            if (migrated.Summary != header.Summary)
            {
                throw new InvalidDataException("Migrated trace summary differs from legacy data.");
            }

            File.Delete(source);
            File.Delete(legacySpans);
        }

        File.Delete(Path.Combine(directory, "index.json"));
    }

    private PerformanceTraceDocument ReadLegacyTrace(PerformanceTraceHeader header, string journal)
    {
        if (header.SchemaVersion != 2)
        {
            throw new InvalidDataException("Unsupported legacy performance trace.");
        }

        var spans = new Dictionary<long, PerformanceSpan>();
        using var reader = new StreamReader(journal, Encoding.UTF8);
        long offset = 0;
        while (offset < header.SpanBytes)
        {
            var line = reader.ReadLine() ?? throw new InvalidDataException("Truncated legacy trace journal.");
            offset += Encoding.UTF8.GetByteCount(line) + 1;
            if (offset > header.SpanBytes)
            {
                throw new InvalidDataException("Invalid legacy trace commit boundary.");
            }

            var span = JsonSerializer.Deserialize(line, CoreJsonReadContext.Default.PerformanceSpan)
                ?? throw new InvalidDataException("Missing legacy trace span.");
            spans[span.Id] = span;
        }

        foreach (var span in header.ActiveSpans)
        {
            spans[span.Id] = span;
        }

        return new PerformanceTraceDocument
        {
            Summary = header.Summary,
            Spans = spans.Values.OrderBy(span => span.Id).ToList(),
            Statistics = header.Statistics,
            Measurements = header.Measurements
        };
    }

    private DenseCloudReport ReadCloud(string file) => ReadLegacy(file, CoreJsonReadContext.Default.DenseCloudReport);

    private T ReadLegacy<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        using var source = File.OpenRead(file);
        return JsonSerializer.Deserialize(source, type) ?? throw new InvalidDataException("Missing legacy artifact.");
    }

    private void MigrateJournal<T>(string target, IBinaryJournalCodec<T> codec, IEnumerable<T> records)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new BinaryJournal<T>(temporary, codec))
            {
                foreach (var record in records)
                {
                    writer.Append(record, durable: false);
                }

                writer.Flush();
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void Write(string file, Action<BinaryWriter> action)
    {
        files.AtomicWrite(file, stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            action(writer);
        });
    }

    private void ReplaceManifestEntry(string directory, string obsolete, string packed)
    {
        var path = Path.Combine(directory, "manifest.json");
        if (File.Exists(path))
        {
            var manifest = json.Read<Artifact>(path);
            manifest.Files.Remove(obsolete);
            manifest.Files[packed] = files.HashFile(Path.Combine(directory, packed));
            files.AtomicJson(path, manifest);
        }
    }
}
