using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Cli;

[Export(typeof(ICliCommand)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PipelineCommand : ICliCommand
{
    private readonly IProjectStoreFactory storeFactory;
    private readonly ICommandOutput output;
    private readonly IProcessor processor;
    private readonly IStageResetService resetService;

    [ImportingConstructor]
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

                output.Write(new PipelinePlanResult
                {
                    SchemaVersion = 1,
                    Revision = store.Load().Revision,
                    Stages = processor.Stages.Take(index + 1),
                    Reuse = "Verified by content fingerprints when the run executes",
                    ActiveImages = store.Load().Images.Count(i => i.Enabled)
                });
                return 0;
            case "reset":
                var affected = resetService.Reset(store, args.Get("from", null) ?? throw new EchoException("--from STAGE is required.", 2));
                output.Write(new PipelineResetResult
                {
                    SchemaVersion = 1,
                    ResetStages = affected,
                    Message = "Next processing run will rebuild these stages. Earlier checkpoints and saved runs are preserved."
                });
                return 0;
            default:
                throw new EchoException("Unknown pipeline action. Use --help.", 2);
        }
    }
}
