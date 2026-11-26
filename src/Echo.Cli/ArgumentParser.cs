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
            "headless",
            "known-project",
            "known-snapshot",
            "all",
            "copy",
            "apply",
            "name",
            "project",
            "set",
            "limit",
            "manifest",
            "events",
            "query",
            "value",
            "target",
            "settings",
            "source",
            "accuracy",
            "colour-tuning",
            "edge-softening",
            "subject-distance",
            "from",
            "artifact",
            "unused",
            "keep-runs",
            "confirm",
            "run",
            "image",
            "image-id",
            "geometry",
            "reference-image-ids",
            "review-image-ids",
            "focal-pixels",
            "sample-step",
            "max-jump",
            "output",
            "format",
            "crs",
            "resolution",
            "relative-heights",
            "max-seam-error",
            "size-mm",
            "tile-depth",
            "detail-tile",
            "leaf-texture-size",
            "jpeg-quality",
            "width",
            "height",
            "view",
            "slice",
            "camera",
            "cameras",
            "mode",
            "lat",
            "lon",
            "alt",
            "alt-datum",
            "yaw",
            "pitch",
            "roll",
            "uncertainty",
            "vertical-uncertainty",
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

            var flag = key is "help" or "json" or "copy" or "summary" or "headless" or "apply" or "all" or "unused" or "relative-heights";
            var value = flag ? "true" : ++i < args.Length ? args[i] : throw new EchoException("Missing value for " + arg, 2);
            if (key == "events" && value is not ("ndjson" or "none"))
            {
                throw new EchoException("Use --events ndjson or --events none.", 2);
            }

            if (!result.Options.TryAdd(key, value))
            {
                throw new EchoException("Duplicate option: " + arg, 2);
            }
        }

        return result;
    }
}
