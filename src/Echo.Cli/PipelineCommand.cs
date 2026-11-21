using Echo.Models;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

public sealed class PipelineCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IProcessor processor;
    private readonly IStageResetService resetService;

    public PipelineCommand(IProjectStoreFactory storeFactory, ICommandOutput output, IProcessor processor, IStageResetService resetService)
    {
        this.storeFactory = storeFactory;
        this.output = output;
        this.processor = processor;
        this.resetService = resetService;
    }

    public IReadOnlyCollection<string> Names => ["pipeline"];

    public int Execute(Arguments args)
    {
        var action = args.Word(1, "");
        var ndjson = args.IsNdjson;
        var store = storeFactory.Open(args.Get("project", null) ?? throw new EchoException("--project PATH is required.", 2));
        store.Load();
        switch (action)
        {
            case "plan":
                var target = args.Get("target", "export")!;
                var index = processor.Stages.ToList().IndexOf(target);
                if (index < 0)
                {
                    throw new EchoException("Unknown target.", 2);
                }

                output.Write(new
                {
                    schemaVersion = 1,
                    revision = store.Load().Revision,
                    stages = processor.Stages.Take(index + 1),
                    reuse = "Verified by content fingerprints when the run executes",
                    activeImages = store.Load().Images.Count(i => i.Enabled)
                });
                return 0;
            case "reset":
                var affected = resetService.Reset(store, args.Get("from", null) ?? throw new EchoException("--from STAGE is required.", 2));
                output.Write(new
                {
                    schemaVersion = 1,
                    resetStages = affected,
                    message = "Next processing run will rebuild these stages. Earlier checkpoints and saved runs are preserved."
                });
                return 0;
            default:
                throw new EchoException("Unknown pipeline action. Use --help.", 2);
        }
    }
}
