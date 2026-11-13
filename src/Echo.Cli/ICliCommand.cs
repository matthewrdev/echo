namespace Echo.Cli;

public interface ICliCommand
{
    IReadOnlyCollection<string> Names
    {
        get;
    }

    int Execute(Arguments arguments);
}
