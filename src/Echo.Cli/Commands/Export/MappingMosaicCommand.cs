using Echo.Export;
using Echo.Models;

namespace Echo.Cli.Commands.Export;

internal sealed class MappingMosaicCommand : ICliCommand
{
    private readonly IMappingMosaicWriter writer;
    private readonly ICommandOutput output;

    public MappingMosaicCommand(IMappingMosaicWriter writer, ICommandOutput output)
    {
        this.writer = writer;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["merge-maps"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("merge-maps",
            "merge-maps MAP_DIRECTORY MAP_DIRECTORY […] --output NEW_DIRECTORY --max-seam-error METRES",
            "Merge overlapping absolute-height mapping exports on the same grid after checking elevation agreement.")
    ];

    public int Execute(Arguments arguments)
    {
        var invalid = arguments.Words.Count < 3
            || arguments.Options.Keys.Any(key => key is not ("output" or "max-seam-error" or "json"));
        if (invalid)
        {
            throw new EchoException("Use merge-maps with at least two mapping export folders.", 2);
        }

        var destination = arguments.Get("output", null) ?? throw new EchoException("--output NEW_DIRECTORY is required.", 2);
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var tolerance = arguments.Number("max-seam-error");
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new EchoException("--max-seam-error must be a positive distance in metres.", 2);
        }

        writer.Write(arguments.Words.Skip(1).ToArray(), destination, tolerance);
        output.Write(new
        {
            directory = destination
        });
        return 0;
    }
}
