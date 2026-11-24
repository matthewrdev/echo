using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Storage.Journals;

namespace Echo.Cli.Commands.Processing;

public sealed class RunCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IFileOperations fileOperations;
    private readonly IProcessor processor;
    private readonly IRunStateRecovery runStateRecovery;
    private readonly IJsonCodec json;

    public RunCommand(IProjectStoreFactory storeFactory,
        ICommandOutput output,
        IFileOperations fileOperations,
        IProcessor processor,
        IRunStateRecovery runStateRecovery,
        IJsonCodec json)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.fileOperations = fileOperations;
        this.processor = processor;
        this.runStateRecovery = runStateRecovery;
        this.json = json;
    }

    public IReadOnlyCollection<string> Names => ["run"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("run start",
            "--project PATH run start [--target STAGE] [--settings FILE] [--events ndjson|none] [--json] [--headless]\n    [--colour-tuning on|off|0..1] [--edge-softening on|off|0..16]\n    [--subject-distance auto|off|METRES]",
            "Start a run without changing the saved project settings. --settings FILE replaces the settings for this run only. Explicit run options apply on top of the file. The default target is alignment.\nStages are features, matching, alignment, depth, cloud, fusion, mesh, texture and export.\n--headless skips live preview files. Saved checkpoints and final outputs are still produced.\nTexture defaults are 0.5 colour tuning and 4 pixels of edge softening."),
        new("run resume",
            "--project PATH run resume RUN_ID [--events ndjson|none] [--json] [--headless]",
            "Resume from saved checkpoints using the original inputs."),
        new("run list",
            "--project PATH run list",
            "List saved runs."),
        new("run status",
            "--project PATH run status [RUN_ID]",
            "Read the run status. The default is latest."),
        new("run cancel",
            "--project PATH run cancel [RUN_ID]",
            "Request cancellation. The default is latest."),
        new("run events",
            "--project PATH run events [RUN_ID]",
            "Read saved run events. The default is latest."),
    ];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        ValidateArguments(args, action);
        var projectPath = args.Get("project", null)
            ?? throw new EchoException("--project PATH is required.", 2);
        var store = storeFactory.Open(projectPath);
        store.Load();

        switch (action)
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
                output.Write(store.ListRuns());
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

    private void ValidateArguments(Arguments args, string action)
    {
        var valid = action switch
        {
            "start" => args.Words.Count == 2 && args.Options.Keys.All(key => key is "project" or "target"
                or "settings" or "events" or "headless" or "colour-tuning" or "edge-softening" or "subject-distance" or "json"),
            "resume" => args.Words.Count == 3 && args.Options.Keys.All(key => key is "project" or "events" or "headless" or "json"),
            "list" => args.Words.Count == 2 && args.Options.Keys.All(key => key is "project" or "json"),
            "status" or "cancel" or "events" => args.Words.Count <= 3 && args.Options.Keys.All(key => key is "project" or "json"),
            _ => false
        };
        if (!valid)
        {
            throw new EchoException("Invalid run arguments. Use run --help.", 2);
        }
    }

    private void ExecuteRun(IProjectStore store, Arguments args, string? resumeId)
    {
        var settingsFile = args.Get("settings", null);
        var settings = settingsFile == null ? null : json.Read<Settings>(settingsFile);
        var textureOptions = TextureRunOptionParser.Parse(args);
        var subjectDistanceOptions = SubjectDistanceRunOptionParser.Parse(args);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var silent = args.Get("events", null) == "none";
            Action<object>? report = silent ? null : value => output.Progress(value, args.IsNdjson);
            var run = processor.Execute(
                store,
                args.Get("target", "alignment")!,
                cancellation.Token,
                resumeId,
                report,
                textureOptions,
                subjectDistanceOptions,
                headless: args.Options.ContainsKey("headless"),
                settings: settings);
            var writeResult = !silent || args.Options.ContainsKey("json");
            if (!args.IsNdjson && writeResult)
            {
                output.Write(run);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
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
        var reader = new BinaryJournalReader<RunEvent>(new RunEventRecordCodec());
        using var log = reader.OpenRead(store.PathFor($"runs/{run.Id}/events.bin"));
        if (log == null)
        {
            return;
        }

        foreach (var entry in reader.Read(log))
        {
            output.Progress(entry, ndjson: true);
        }
    }
}
