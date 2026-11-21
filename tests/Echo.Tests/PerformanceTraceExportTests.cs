using System.Text.Json;
using Echo.Cli;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Telemetry;

namespace Echo.Tests;

public sealed class PerformanceTraceExportTests
{
    private readonly IPerformanceTrace trace;
    private readonly IPerformanceTraceStore traces;
    private readonly IPerformanceTraceExporter exporter;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IArgumentParser parser;
    private readonly ICommandOutput output;

    public PerformanceTraceExportTests(IPerformanceTrace trace, IPerformanceTraceStore traces, IPerformanceTraceExporter exporter,
        IProjectStoreFactory stores, IFileOperations files, IJsonCodec json, IArgumentParser parser, ICommandOutput output)
    {
        this.trace = trace;
        this.traces = traces;
        this.exporter = exporter;
        this.stores = stores;
        this.files = files;
        this.json = json;
        this.parser = parser;
        this.output = output;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Trace CLI exports complete JSON and safely embedded standalone HTML", () => WithTrace((store, document) =>
        {
            var command = new TraceCommand(stores, traces, output, exporter);
            var run = document.Summary.RunId;
            var attempt = document.Summary.Id;
            var directory = store.PathFor($"runs/{run}");
            var journal = Path.Combine(directory, "traces", attempt + ".spans.ndjson");
            var header = Path.Combine(directory, "traces", attempt + ".json");
            var original = File.ReadAllBytes(journal);
            var originalHeader = File.ReadAllBytes(header);
            var destination = store.PathFor("exports/full trace.json");
            Check(command.Execute(parser.Parse(["--project", store.Root, "trace", "export", attempt, "--run", run, "--output", destination, "--format", "json"])) == 0, "JSON command succeeds");
            var exported = json.Read<PerformanceTraceDocument>(destination);
            Check(json.Encode(exported) == json.Encode(traces.Read(directory, attempt)), "JSON retains every span, aggregate and measurement");
            Check(exported.Summary.Status == PerformanceStatus.Running, "Export does not mark a live trace completed");
            var html = store.PathFor("exports/full trace.html");
            Check(command.Execute(parser.Parse(["--project", store.Root, "trace", "export", attempt, "--run", run, "--output", html])) == 0, "HTML extension infers the format");
            var text = File.ReadAllText(html);
            const string marker = "<script type=\"application/json\" id=\"trace-data\">";
            var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = text.IndexOf("</script>", start, StringComparison.Ordinal);
            var embedded = JsonSerializer.Deserialize<PerformanceTraceDocument>(text[start..end], json.Options)!;
            Check(json.Encode(embedded) == json.Encode(exported), "HTML contains the same complete trace");
            Check(!text.Contains("</script><script>alert", StringComparison.Ordinal), "Labels cannot terminate the JSON script element");
            Check(!text.Contains("__ECHO_TRACE_DATA__", StringComparison.Ordinal), "Viewer placeholder replaced");
            Check(!text.Contains("src=", StringComparison.Ordinal) && !text.Contains("href=", StringComparison.Ordinal), "Viewer has no external assets");
            Check(File.ReadAllBytes(journal).SequenceEqual(original) && File.ReadAllBytes(header).SequenceEqual(originalHeader), "Export leaves the source untouched");
            Check(!Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories).Any(), "No temporary exports remain");
        }));
        tests.Run("Trace export preserves existing destinations and rejects unsupported formats", () => WithTrace((store, document) =>
        {
            var directory = store.PathFor($"runs/{document.Summary.RunId}");
            var destination = store.PathFor("existing.json");
            File.WriteAllText(destination, "keep");
            Reject(() => exporter.Export(directory, document.Summary.Id, destination, "json"));
            Check(File.ReadAllText(destination) == "keep", "Existing output preserved");
            var invalid = store.PathFor("invalid.txt");
            Reject(() => exporter.Export(directory, document.Summary.Id, invalid, "unknown"));
            Check(!File.Exists(invalid), "Invalid format creates no output");
        }));
        tests.Run("Trace summary reads aggregates without opening the span journal", () => WithTrace((store, document) =>
        {
            var directory = store.PathFor($"runs/{document.Summary.RunId}");
            var journal = Path.Combine(directory, "traces", document.Summary.Id + ".spans.ndjson");
            File.Move(journal, journal + ".saved");
            var summary = traces.Read(directory, document.Summary.Id, includeSpans: false);
            Check(summary.Spans.Count == 0 && summary.Statistics.Count > 0 && summary.Measurements.Count == 1, "Summary is independently readable");
            Check(summary.Statistics.Single(row => row.Name.StartsWith("pair", StringComparison.Ordinal)).P95UpperMilliseconds >= 0, "New diagnostics round-trip");
            try
            {
                traces.Read(directory, document.Summary.Id);
                throw new InvalidOperationException("Full read should require the journal");
            }
            catch (FileNotFoundException)
            {
            }
        }));
    }

    private void WithTrace(Action<IProjectStore, PerformanceTraceDocument> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-trace-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = stores.Open(path);
            store.Init();
            var run = new RunData { Id = Guid.NewGuid().ToString("N"), Status = "running", Target = "matching" };
            files.AtomicJson(store.PathFor($"runs/{run.Id}/run.json"), run);
            using var session = trace.BeginRun(run.Id, "matching");
            using var stage = trace.Begin("matching", PerformanceSpanKind.Stage, stage: "matching");
            using (var pair = trace.Begin("pair </script><script>alert('unsafe')</script> Ω", PerformanceSpanKind.Operation))
            {
                pair!.Measure(100, 256, 2.5);
                trace.Measurement("descriptors", 1000, "descriptors");
            }
            var document = session.Snapshot();
            traces.Save(store.PathFor($"runs/{run.Id}"), document);
            action(store, document);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void Reject(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected a usage error");
        }
        catch (EchoException error)
        {
            Check(error.ExitCode == 2, "Usage exit code");
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
