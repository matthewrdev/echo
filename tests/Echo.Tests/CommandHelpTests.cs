using Echo.Cli;

namespace Echo.Tests;

public sealed class CommandHelpTests
{
    private readonly ICommandApplication application;

    public CommandHelpTests(ICommandApplication application)
    {
        this.application = application;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("CLI help drills down through command groups and actions", () =>
        {
            var overview = Capture(["--help"]);
            var group = Capture(["images", "--help"]);
            var action = Capture(["project", "a project.echo", "images", "metadata", "--help"]);

            if (!overview.Contains("  images", StringComparison.Ordinal)
                || !group.Contains("images import", StringComparison.Ordinal)
                || !group.Contains("images metadata", StringComparison.Ordinal)
                || !action.Contains("images metadata IMAGE_ID", StringComparison.Ordinal)
                || action.Contains("images import", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected CLI help at one of the command levels.");
            }
        });
    }

    private string Capture(string[] arguments)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            if (application.Run(arguments) != 0)
            {
                throw new InvalidOperationException("Expected CLI help to succeed.");
            }

            return output.ToString();
        }
        finally
        {
            Console.SetOut(previous);
        }
    }
}
