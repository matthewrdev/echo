using System.Text;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Telemetry;

public sealed class PerformanceTraceStore : IPerformanceTraceStore
{
    private readonly IFileOperations files;
    private readonly object gate = new();
    private readonly PerformanceTraceHeaderCodec headers = new();
    private readonly PerformanceSpanCodec spans = new();

    public PerformanceTraceStore(IFileOperations files)
    {
        this.files = files;
    }

    public void RecoverInterrupted(string runDirectory, bool cancelled)
    {
        foreach (var summary in List(runDirectory).Where(item => item.Status == PerformanceStatus.Running))
        {
            var path = TracePath(runDirectory, summary.Id);
            var header = ReadHeader(path);
            var status = cancelled ? PerformanceStatus.Cancelled : PerformanceStatus.Interrupted;
            header.Summary.Status = status;
            foreach (var span in header.ActiveSpans)
            {
                span.Status = status;
            }

            // Only the formerly active spans need to be appended.
            Save(runDirectory, new PerformanceTraceDocument
            {
                Summary = header.Summary,
                Spans = header.ActiveSpans,
                Statistics = header.Statistics,
                Measurements = header.Measurements
            });
        }
    }

    public void Save(string runDirectory, PerformanceTraceDocument trace)
    {
        lock (gate)
        {
            var path = TracePath(runDirectory, trace.Summary.Id);
            var committed = File.Exists(path) ? ReadCommittedLength(path) : sizeof(uint);
            var journalPath = Path.ChangeExtension(path, ".spans.bin");
            long length;
            using (var journal = new BinaryJournal<PerformanceSpan>(journalPath, spans, committed))
            {
                foreach (var span in trace.Spans)
                {
                    if (span.Status != PerformanceStatus.Running)
                    {
                        journal.Append(span, durable: false);
                    }
                }

                length = journal.Length;
                if (length != committed)
                {
                    journal.Flush();
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
            files.AtomicWrite(path, stream =>
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                headers.Write(writer, header);
            });
        }
    }

    public IReadOnlyList<PerformanceTraceSummary> List(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "traces");
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.trace.bin").Select(ReadSummary).OrderByDescending(item => item.StartedAt).ToArray()
            : [];
    }

    public PerformanceTraceDocument Read(string runDirectory, string attemptId, bool includeSpans = true)
    {
        var path = TracePath(runDirectory, attemptId);
        var header = ReadHeader(path);
        var result = new PerformanceTraceDocument
        {
            Summary = header.Summary,
            Statistics = header.Statistics,
            Measurements = header.Measurements
        };
        if (includeSpans)
        {
            var reader = new BinaryJournalReader<PerformanceSpan>(spans);
            using var stream = reader.OpenRead(Path.ChangeExtension(path, ".spans.bin"))
                ?? throw new FileNotFoundException("Missing performance trace journal.");
            result.Spans = reader.Read(stream, header.SpanBytes)
                .Concat(header.ActiveSpans).OrderBy(span => span.Id).ToList();
        }

        return result;
    }

    private PerformanceTraceHeader ReadHeader(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        return headers.Read(reader);
    }

    private long ReadCommittedLength(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        headers.ReadSummary(reader);
        return reader.ReadInt64();
    }

    private PerformanceTraceSummary ReadSummary(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        return headers.ReadSummary(reader);
    }

    private string TracePath(string runDirectory, string attemptId)
    {
        if (!Guid.TryParseExact(attemptId, "N", out _))
        {
            throw new EchoException("Invalid performance trace identifier.", 2);
        }

        return Path.Combine(runDirectory, "traces", attemptId + ".trace.bin");
    }
}
