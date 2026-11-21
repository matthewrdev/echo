using Echo.Models;

namespace Echo.Cli;

public sealed class ArgumentParser : IArgumentParser
{
    public ArgumentParser()
    {
    }

    public Arguments Parse(string[] args)
    {
        var result = new Arguments();
        var supported = new HashSet<string>
        {
            "help",
            "json",
            "summary",
            "all",
            "copy",
            "apply",
            "name",
            "project",
            "set",
            "limit",
            "events",
            "query",
            "value",
            "target",
            "colour-tuning",
            "edge-softening",
            "subject-distance",
            "from",
            "artifact",
            "unused",
            "keep-runs",
            "confirm",
            "run",
            "output",
            "format",
            "width",
            "height",
            "view",
            "mode",
            "lat",
            "lon",
            "alt",
            "alt-datum",
            "yaw",
            "pitch",
            "roll",
            "uncertainty",
            "ax",
            "ay",
            "bx",
            "by"
        };
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h")
            {
                arg = "--help";
            }

            if (arg is "-p")
            {
                arg = "--project";
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Words.Add(arg);
                continue;
            }

            var key = arg[2..];
            if (!supported.Contains(key))
            {
                throw new EchoException("Unknown option: " + arg, 2);
            }

            var flag = key is "help" or "json" or "copy" or "summary" or "apply" or "all" or "unused";
            var value = flag ? "true" : ++i < args.Length ? args[i] : throw new EchoException("Missing value for " + arg, 2);
            if (!result.Options.TryAdd(key, value))
            {
                throw new EchoException("Duplicate option: " + arg, 2);
            }
        }

        return result;
    }
}
