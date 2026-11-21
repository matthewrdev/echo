namespace Echo.Cli;

public interface ICliCommand
{
    IReadOnlyCollection<string> Names
    {
        get;
    }

    IReadOnlyList<CommandUsage> Usage
    {
        get;
    }

    int Execute(Arguments arguments);
}
