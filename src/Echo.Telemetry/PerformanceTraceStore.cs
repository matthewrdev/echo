using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Telemetry;

[Export(typeof(IPerformanceTraceStore)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PerformanceTraceStore : IPerformanceTraceStore
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly object gate = new();

    [ImportingConstructor]
    public PerformanceTraceStore(IJsonCodec json, IFileOperations files)
    {
        this.json = json;
        this.files = files;
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
            files.AtomicJson(TracePath(runDirectory, trace.Summary.Id), trace);
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

    public PerformanceTraceDocument Read(string runDirectory, string attemptId) => json.Read<PerformanceTraceDocument>(TracePath(runDirectory, attemptId));

    private string TracePath(string runDirectory, string attemptId)
    {
        if (!Guid.TryParseExact(attemptId, "N", out _))
        {
            throw new EchoException("Invalid performance trace identifier.", 2);
        }
        return Path.Combine(runDirectory, "traces", attemptId + ".json");
    }
}
