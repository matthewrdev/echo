using Echo.Pipeline.Runs;
namespace Echo.Pipeline;

public interface IPipelineStage
{
    string Name
    {
        get;
    }

    int Order
    {
        get;
    }

    void Execute(RunContext context);
}
