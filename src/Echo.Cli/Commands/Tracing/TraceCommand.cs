using Echo.Models;
using Echo.Projects;
using Echo.Telemetry;

namespace Echo.Cli.Commands.Tracing;

public sealed class TraceCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IPerformanceTraceStore traces;
    private readonly ICommandOutput output;
    private readonly IPerformanceTraceExporter exporter;

    public TraceCommand(IProjectStoreFactory stores, IPerformanceTraceStore traces, ICommandOutput output, IPerformanceTraceExporter exporter)
    {
        this.stores = stores;
        this.traces = traces;
        this.output = output;
        this.exporter = exporter;
    }

    public IReadOnlyCollection<string> Names => ["trace"];

    public int Execute(Arguments args)
    {
        var store = stores.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        if (args.Word(1, "list") == "list" && args.Options.ContainsKey("all"))
        {
            var runs = store.PathFor("runs");
            var summaries = Directory.Exists(runs)
                ? Directory.GetDirectories(runs).SelectMany(traces.List).OrderByDescending(item => item.StartedAt).ToArray()
                : [];
            output.Write(summaries);
            return 0;
        }
        var run = store.GetRun(args.Get("run", "latest")!);
        var directory = store.PathFor($"runs/{run.Id}");
        switch (args.Word(1, "list"))
        {
            case "list":
                output.Write(traces.List(directory));
                break;
            case "show":
                output.Write(traces.Read(directory, args.RequiredWord(2), includeSpans: !args.Options.ContainsKey("summary")));
                break;
            case "export":
                var destination = args.Get("output", null) ?? throw new EchoException("--output FILE is required.", 2);
                var format = args.Get("format", null) ?? (Path.GetExtension(destination).Equals(".html", StringComparison.OrdinalIgnoreCase) ? "html" : "json");
                output.Write(exporter.Export(directory, args.RequiredWord(2), destination, format));
                break;
            default:
                throw new EchoException("Use trace list, trace show ATTEMPT_ID, or trace export ATTEMPT_ID --output FILE --format html|json.", 2);
        }
        return 0;
    }
}
