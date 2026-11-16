using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Projects;
using Echo.Telemetry;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TraceCommand : ICliCommand
{
    private readonly IProjectStoreFactory stores;
    private readonly IPerformanceTraceStore traces;
    private readonly ICommandOutput output;

    [ImportingConstructor]
    public TraceCommand(IProjectStoreFactory stores, IPerformanceTraceStore traces, ICommandOutput output)
    {
        this.stores = stores;
        this.traces = traces;
        this.output = output;
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
                output.Write(traces.Read(directory, args.RequiredWord(2)));
                break;
            default:
                throw new EchoException("Use trace list or trace show ATTEMPT_ID --run RUN_ID.", 2);
        }
        return 0;
    }
}
