namespace Echo.Export;

public interface IMappingTools
{
    string Run(string executable, params string[] arguments);
}
