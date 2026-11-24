using Echo.Cli.Commands.Hints;
using Echo.Cli;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;

namespace Echo.Tests;

public sealed class GisCommandTests
{
    private readonly ICommandApplication application;
    private readonly IArgumentParser parser;
    private readonly IHintFactory hints;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    public GisCommandTests(ICommandApplication application, IArgumentParser parser, IHintFactory hints, IProjectStoreFactory stores, IFileOperations files)
    {
        this.application = application;
        this.parser = parser;
        this.hints = hints;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Desktop geographic export command accepts its output folder and selected run", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-gis-command-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init("GIS command test");
            try
            {
                var id = Guid.NewGuid().ToString("N");
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), new ReconstructionScene
                {
                    Origin = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 87.202 }
                });
                store.SaveRun(new RunData
                {
                    Id = id,
                    Snapshot = store.Load(),
                    Status = "completed",
                    Artifacts = new Dictionary<string, string> { ["alignment"] = "artifacts/alignment/test" }
                });
                var destination = Path.Combine(root, "GIS export");
                Check(application.Run(["--project", root, "export-gis", "--run", id, "--output", destination]) == 0, "Exact desktop command succeeds");
                Check(File.Exists(Path.Combine(destination, "georeference.json")), "Reference file written");
                Check(application.Run(["--project", root, "export-gis", "--run", id]) == 2, "Output folder is required");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
        tests.Run("Camera hint CLI retains the explicit altitude reference without converting the value", () =>
        {
            var arguments = parser.Parse(["hints", "camera", "photo", "--lat", "-33", "--lon", "150", "--alt", "87.202", "--alt-datum", "WGS84 ellipsoid"]);
            var hint = hints.Create(arguments, "camera");
            Check(hint.Position?.AltitudeDatum == "WGS84 ellipsoid" && hint.Position.Altitude == 87.202, "Explicit reference and height preserved");
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
