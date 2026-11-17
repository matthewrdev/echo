using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RunCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IJsonCodec jsonCodec;
    private readonly IFileOperations fileOperations;
    private readonly IProcessor processor;
    private readonly IRunStateRecovery runStateRecovery;

    [ImportingConstructor]
    public RunCommand(IProjectStoreFactory storeFactory,
        ICommandOutput output,
        IJsonCodec jsonCodec,
        IFileOperations fileOperations,
        IProcessor processor,
        IRunStateRecovery runStateRecovery)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.processor = processor;
        this.runStateRecovery = runStateRecovery;
    }

    public IReadOnlyCollection<string> Names => ["run"];

    public int Execute(Arguments args)
    {
        var projectPath = args.Get("project", null)
            ?? throw new EchoException("--project PATH is required.", 2);
        var store = storeFactory.Open(projectPath);
        store.Load();

        switch (args.Word(1, ""))
        {
            case "start":
                ExecuteRun(store, args, resumeId: null);
                break;
            case "resume":
                ExecuteRun(store, args, args.RequiredWord(2));
                break;
            case "status":
                runStateRecovery.Recover(store);
                output.Write(store.GetRun(args.Word(2, "latest")));
                break;
            case "list":
                runStateRecovery.Recover(store);
                output.Write(ListRuns(store));
                break;
            case "cancel":
                CancelRun(store, args.Word(2, "latest"));
                break;
            case "events":
                WriteEvents(store, args.Word(2, "latest"));
                break;
            default:
                throw new EchoException("Unknown run action. Use --help.", 2);
        }

        return 0;
    }

    private void ExecuteRun(IProjectStore store, Arguments args, string? resumeId)
    {
        var textureOptions = TextureRunOptionParser.Parse(args);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var run = processor.Execute(
                store,
                args.Get("target", "alignment")!,
                cancellation.Token,
                resumeId,
                value => output.Progress(value, args.IsNdjson),
                textureOptions);
            if (!args.IsNdjson)
            {
                output.Write(run);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private IReadOnlyList<RunSummary> ListRuns(IProjectStore store)
    {
        var directory = store.PathFor("runs");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "run.json", SearchOption.AllDirectories)
            .Select(jsonCodec.Read<RunData>)
            .OrderByDescending(run => run.Created)
            .Select(run => new RunSummary
            {
                Id = run.Id,
                Status = run.Status,
                Target = run.Target,
                Created = run.Created
            })
            .ToArray();
    }

    private void CancelRun(IProjectStore store, string id)
    {
        var run = store.GetRun(id);
        if (run.Status != "running")
        {
            throw new EchoException("Run is not active.");
        }

        fileOperations.AtomicBytes(store.PathFor($"runs/{run.Id}/cancel"), [1]);
        runStateRecovery.Recover(store);
        output.Write(new
        {
            runId = run.Id,
            cancellationRequested = true
        });
    }

    private void WriteEvents(IProjectStore store, string id)
    {
        var run = store.GetRun(id);
        using var log = File.OpenText(store.PathFor($"runs/{run.Id}/events.ndjson"));
        var buffer = new char[16384];
        int count;
        while ((count = log.Read(buffer)) != 0)
        {
            Console.Out.Write(buffer, 0, count);
        }
    }
}
