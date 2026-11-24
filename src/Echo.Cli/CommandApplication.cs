using System.Text.Json;
using Echo.Models;

namespace Echo.Cli;

public sealed class CommandApplication : ICommandApplication
{
    private readonly IArgumentParser argumentParser;
    private readonly ICommandOutput output;
    private readonly IEnumerable<ICliCommand> commands;

    public CommandApplication(IArgumentParser argumentParser, ICommandOutput output, IEnumerable<ICliCommand> commands)
    {
        this.argumentParser = argumentParser;
        this.output = output;
        this.commands = commands;
    }

    public int Run(string[] input)
    {
        try
        {
            var arguments = argumentParser.Parse(input);
            NormalizeProjectCommand(arguments);

            var help = arguments.Options.ContainsKey("help") || arguments.Words.Count == 0;
            if (help && arguments.Words.Count == 0)
            {
                WriteHelp();
                return 0;
            }

            var commandName = arguments.Words[0];
            var command = commands.SingleOrDefault(candidate => candidate.Names.Contains(commandName));
            if (command == null)
            {
                throw new EchoException("Unknown command. Use --help.", 2);
            }

            if (help)
            {
                WriteHelp(command, arguments);
                return 0;
            }

            return command.Execute(arguments);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Processing stopped. Saved results are still available. Resume reuses saved work and repeats unfinished steps.");
            return 130;
        }
        catch (Exception error)
        {
            var code = error switch
            {
                EchoException echo => echo.ExitCode,
                FormatException or JsonException or ArgumentException => 2,
                _ => 4
            };
            output.Error(error.Message, code);
            return code;
        }
    }

    private void NormalizeProjectCommand(Arguments arguments)
    {
        var isProjectPath = arguments.Words.Count >= 3
            && arguments.Words[0] == "project"
            && arguments.Words[1] is not ("init" or "info" or "rename" or "delete");
        if (!isProjectPath)
        {
            return;
        }

        var group = arguments.Words[2];
        var isProjectCommand = group is "images" or "sets" or "storage" or "config" or "hints" or "gps-polygons"
            or "pipeline" or "run" or "trace" or "inspect" or "geo" or "export-gis" or "render";
        if (!isProjectCommand)
        {
            return;
        }

        if (!arguments.Options.TryAdd("project", arguments.Words[1]))
        {
            throw new EchoException("Specify the project path once.", 2);
        }

        arguments.Words.RemoveRange(0, 2);
    }

    private void WriteHelp()
    {
        Console.WriteLine("Echo photogrammetry workbench");
        Console.WriteLine();
        Console.WriteLine("Usage: echo-photo [--project PATH] COMMAND [ACTION] [OPTIONS]");
        Console.WriteLine("Project commands also accept echo-photo project PATH COMMAND [ACTION].");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (var name in commands.SelectMany(command => command.Names).Order(StringComparer.Ordinal))
        {
            Console.WriteLine("  " + name);
        }

        Console.WriteLine();
        Console.WriteLine("Pass --help after a command or action for its usage.");
        Console.WriteLine("Data responses are JSON. Use --events ndjson for live processing events.");
    }

    private void WriteHelp(ICliCommand command, Arguments arguments)
    {
        var action = arguments.Word(1, "");
        var path = arguments.Words[0];
        var hasActions = command.Usage.Any(entry => entry.Path.StartsWith(path + " ", StringComparison.Ordinal));
        var query = hasActions && action.Length != 0 ? path + " " + action : path;
        var usage = command.Usage.Where(entry => entry.Path == query
            || entry.Path.StartsWith(query + " ", StringComparison.Ordinal)).ToArray();
        if (usage.Length == 0)
        {
            throw new EchoException("Unknown action. Use " + path + " --help.", 2);
        }

        foreach (var entry in usage)
        {
            Console.WriteLine("  echo-photo " + entry.Syntax);
            Console.WriteLine(entry.Description);
            Console.WriteLine();
        }
    }
}
