using Echo.Contracts;
using Echo.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Echo.Telemetry;

public sealed class PerformanceTraceStore : IPerformanceTraceStore
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly object gate = new();
    private readonly JsonSerializerOptions compact;
    private readonly CoreJsonReadContext metadata;

    public PerformanceTraceStore(IJsonCodec json, IFileOperations files)
    {
        this.json = json;
        this.files = files;
        compact = new JsonSerializerOptions(json.Options) { WriteIndented = false };
        metadata = new CoreJsonReadContext(compact);
    }

    public void RecoverInterrupted(string runDirectory, bool cancelled)
    {
        foreach (var summary in List(runDirectory).Where(item => item.Status == PerformanceStatus.Running))
        {
            var document = Read(runDirectory, summary.Id);
            var status = cancelled ? PerformanceStatus.Cancelled : PerformanceStatus.Interrupted;
            document.Summary.Status = status;
            foreach (var span in document.Spans.Where(item => item.Status == PerformanceStatus.Running))
            {
                span.Status = status;
            }
            // Keep the last measured durations; the worker's exit time is unknown.
            Save(runDirectory, document);
        }
    }

    public void Save(string runDirectory, PerformanceTraceDocument trace)
    {
        lock (gate)
        {
            var directory = Path.Combine(runDirectory, "traces");
            Directory.CreateDirectory(directory);
            var path = TracePath(runDirectory, trace.Summary.Id);
            var previous = File.Exists(path) ? json.Read<PerformanceTraceHeader>(path) : null;
            var committed = previous?.SchemaVersion == 2 ? previous.SpanBytes : 0;
            var journal = Path.ChangeExtension(path, ".spans.ndjson");
            long length;
            using (var stream = new FileStream(journal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                if (committed < 0 || committed > stream.Length)
                {
                    throw new InvalidDataException("Truncated performance trace journal.");
                }
                // The header is the commit record. Discard an interrupted append before retrying.
                stream.SetLength(committed);
                stream.Position = committed;
                foreach (var span in trace.Spans.Where(span => span.Status != PerformanceStatus.Running))
                {
                    JsonSerializer.Serialize(stream, span, metadata.PerformanceSpan);
                    stream.WriteByte((byte)'\n');
                }
                length = stream.Position;
                if (length != committed)
                {
                    stream.Flush(true);
                }
            }
            var header = new PerformanceTraceHeader
            {
                Summary = trace.Summary,
                SpanBytes = length,
                ActiveSpans = trace.Spans.Where(span => span.Status == PerformanceStatus.Running).ToList(),
                Statistics = trace.Statistics,
                Measurements = trace.Measurements
            };
            files.AtomicWrite(path, stream => JsonSerializer.Serialize(stream, header, metadata.PerformanceTraceHeader));
            var summaries = List(runDirectory).Where(item => item.Id != trace.Summary.Id).ToList();
            summaries.Add(trace.Summary);
            files.AtomicJson(Path.Combine(directory, "index.json"), summaries);
        }
    }

    public IReadOnlyList<PerformanceTraceSummary> List(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "traces", "index.json");
        return File.Exists(path) ? json.Read<List<PerformanceTraceSummary>>(path).OrderByDescending(item => item.StartedAt).ToArray() : [];
    }

    public PerformanceTraceDocument Read(string runDirectory, string attemptId, bool includeSpans = true)
    {
        var path = TracePath(runDirectory, attemptId);
        var header = json.Read<PerformanceTraceHeader>(path);
        if (header.SchemaVersion == 1)
        {
            var legacy = json.Read<PerformanceTraceDocument>(path);
            if (!includeSpans)
            {
                legacy.Spans.Clear();
            }
            return legacy;
        }
        if (header.SchemaVersion != 2 || header.SpanBytes < 0)
        {
            throw new InvalidDataException("Unsupported performance trace storage.");
        }
        if (!includeSpans)
        {
            return new PerformanceTraceDocument { Summary = header.Summary, Statistics = header.Statistics, Measurements = header.Measurements };
        }
        var spans = new Dictionary<long, PerformanceSpan>();
        using var stream = new FileStream(Path.ChangeExtension(path, ".spans.ndjson"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (header.SpanBytes > stream.Length)
        {
            throw new InvalidDataException("Truncated performance trace journal.");
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        long consumed = 0;
        while (consumed < header.SpanBytes)
        {
            var line = reader.ReadLine() ?? throw new InvalidDataException("Truncated performance trace record.");
            consumed += Encoding.UTF8.GetByteCount(line) + 1;
            if (consumed > header.SpanBytes)
            {
                throw new InvalidDataException("Invalid performance trace commit boundary.");
            }
            var span = JsonSerializer.Deserialize(line, metadata.PerformanceSpan)
                ?? throw new InvalidDataException("Empty performance span.");
            spans[span.Id] = span;
        }
        foreach (var span in header.ActiveSpans)
        {
            spans[span.Id] = span;
        }
        // Keep the CLI/native viewer transport contract independent of journal storage.
        return new PerformanceTraceDocument
        {
            Summary = header.Summary,
            Spans = spans.Values.OrderBy(span => span.Id).ToList(),
            Statistics = header.Statistics,
            Measurements = header.Measurements
        };
    }

    private string TracePath(string runDirectory, string attemptId)
    {
        if (!Guid.TryParseExact(attemptId, "N", out _))
        {
            throw new EchoException("Invalid performance trace identifier.", 2);
        }
        return Path.Combine(runDirectory, "traces", attemptId + ".json");
    }
}
