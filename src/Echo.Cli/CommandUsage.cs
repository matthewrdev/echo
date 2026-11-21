namespace Echo.Cli;

public sealed record CommandUsage(
    string Path,
    string Syntax,
    string Description);
