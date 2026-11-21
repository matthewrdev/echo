namespace Echo.Models;

public sealed class EchoException : Exception
{
    public EchoException(string message, int exitCode = 3) : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode
    {
        get;
    }
}
