namespace Echo.Cli;

public interface IArgumentParser
{
    Arguments Parse(string[] arguments);
}
