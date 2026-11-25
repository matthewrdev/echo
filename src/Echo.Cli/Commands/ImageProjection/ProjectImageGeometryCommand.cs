using System.Globalization;
using System.Text.Json;
using Echo.ImageProjection;
using Echo.Models;

namespace Echo.Cli.Commands.ImageProjection;

internal sealed class ProjectImageGeometryCommand : ICliCommand
{
    private readonly IImageGeometryProjection projection;
    private readonly ICommandOutput output;

    public ProjectImageGeometryCommand(IImageGeometryProjection projection, ICommandOutput output)
    {
        this.projection = projection;
        this.output = output;
    }

    public IReadOnlyCollection<string> Names => ["project-image-geometry"];

    public IReadOnlyList<CommandUsage> Usage =>
    [
        new("project-image-geometry",
            "--project PATH project-image-geometry (--image FILE | --image-id ID) --geometry FILE.json --output NEW_FILE.json\n"
            + "    [--run RUN_ID] [--sample-step PIXELS] [--max-jump MODEL_UNITS]\n"
            + "    [--reference-image-ids ID,ID] [--focal-pixels PIXELS] [--review-image-ids ID,ID]",
            "Project image-space points, polylines and polygon outlines onto a saved mesh. "
            + "External images can use overlapping capture matches or controlPoints in the geometry file."),
    ];

    public int Execute(Arguments arguments)
    {
        var invalid = arguments.Words.Count != 1 || arguments.Options.Keys.Any(key => key is not
            ("project" or "run" or "image" or "image-id" or "geometry" or "output" or "reference-image-ids"
            or "review-image-ids" or "focal-pixels" or "sample-step" or "max-jump" or "json"));
        if (invalid)
        {
            throw new EchoException("Invalid projection arguments. Use project-image-geometry --help.", 2);
        }

        var project = Required(arguments, "project");
        var destination = Path.GetFullPath(Required(arguments, "output"));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new EchoException("Choose a new projection output path.", 2);
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var request = Request(arguments, project);
            var result = projection.Project(request, cancellation.Token);
            Write(destination, result, cancellation.Token);
            output.Write(new
            {
                result.RunId,
                Output = destination,
                Features = result.Features.Count,
                ReviewFeatures = result.Features.Count(item => item.ReviewFlags.Count > 0)
            });
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }

        return 0;
    }

    private ImageGeometryRequest Request(Arguments arguments, string project)
    {
        var source = Required(arguments, "geometry");
        using var stream = File.OpenRead(source);
        var document = JsonSerializer.Deserialize(stream, ImageProjectionJsonContext.Default.ImageGeometryDocument)
            ?? throw new EchoException("The geometry document is empty.", 2);
        return new ImageGeometryRequest(project, arguments.Get("run", "latest")!, arguments.Get("image", null),
            arguments.Get("image-id", null), document, Number(arguments, "sample-step") ?? 4,
            Number(arguments, "max-jump"), Ids(arguments, "reference-image-ids"), Number(arguments, "focal-pixels"),
            Ids(arguments, "review-image-ids"));
    }

    private void Write(string destination, ImageGeometryResult result, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, result, ImageProjectionJsonContext.Default.ImageGeometryResult);
            }

            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string[] Ids(Arguments arguments, string key) =>
        arguments.Get(key, null)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private string Required(Arguments arguments, string key) =>
        arguments.Get(key, null) ?? throw new EchoException($"--{key} is required.", 2);

    private double? Number(Arguments arguments, string key) => arguments.Get(key, null) is { } value
        ? double.Parse(value, CultureInfo.InvariantCulture) : null;
}
