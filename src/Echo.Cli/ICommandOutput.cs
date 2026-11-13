namespace Echo.Cli;

public interface ICommandOutput
{
    void Write(object value);
    void Progress(object value, bool ndjson);
    void Error(string message, int exitCode);
}
