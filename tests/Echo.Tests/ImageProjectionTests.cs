using System.Text.Json;
using Echo.Cli;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class ImageProjectionTests
{
    private readonly ICommandApplication application;
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;

    public ImageProjectionTests(ICommandApplication application, IProjectStoreFactory stores, IFileOperations files)
    {
        this.application = application;
        this.stores = stores;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Image geometry CLI preserves first hits, mesh misses, closed outlines and review visibility", ProjectGeometry);
    }

    private void ProjectGeometry()
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-image-projection-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Image projection fixture");
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var snapshot = store.Load();
            snapshot.Images.Add(new Photo { Id = "camera", Width = 100, Height = 100, FocalPixels = 100 });
            files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "camera" }]
            });
            files.AtomicData(store.PathFor("artifacts/mesh/test/mesh.json"), new Surface
            {
                Vertices = [[-1, -1, 2], [1, -1, 2], [0, 1, 2], [-2, -2, 4], [2, -2, 4], [0, 2, 4]],
                Faces = [[3, 4, 5], [0, 1, 2]]
            });
            store.SaveRun(new RunData
            {
                Id = id,
                Snapshot = snapshot,
                Status = "completed",
                Artifacts = new Dictionary<string, string>
                {
                    ["alignment"] = "artifacts/alignment/test",
                    ["mesh"] = "artifacts/mesh/test"
                }
            });
            var input = store.PathFor("geometry.json");
            File.WriteAllText(input, """
                {"features":[
                  {"id":"point","geometry":"point","pixels":[[50,50]]},
                  {"id":"line","geometry":"polyline","pixels":[[50,50],[99,99]]},
                  {"id":"area","geometry":"polygon","pixels":[[50,50],[60,50],[50,60]]}
                ]}
                """);
            var destination = store.PathFor("result.json");
            string[] command = ["project", root, "project-image-geometry", "--run", id, "--image-id", "camera",
                "--geometry", input, "--output", destination, "--review-image-ids", "camera"];
            Check(application.Run(command) == 0, "Projection should succeed against the saved mesh.");
            using var output = JsonDocument.Parse(File.ReadAllText(destination));
            var result = output.RootElement;
            var features = result.GetProperty("features");
            var first = features[0].GetProperty("samples")[0];
            Check(first.GetProperty("position")[2].GetDouble() == 2, "Nearest triangle must win.");
            Check(first.GetProperty("normal")[2].GetDouble() == 1, "Return the triangle normal.");
            var samples = features[1].GetProperty("samples").EnumerateArray().ToArray();
            Check(samples[^1].GetProperty("position").ValueKind == JsonValueKind.Null
                && samples[^1].GetProperty("breakBefore").GetBoolean(), "A miss must break the line.");
            var polygon = features[2].GetProperty("samples").EnumerateArray().ToArray();
            Check(polygon[0].GetProperty("pixel").GetRawText() == polygon[^1].GetProperty("pixel").GetRawText(),
                "The polygon outline must close.");
            var review = result.GetProperty("reviewImages")[0].GetProperty("features")[0].GetProperty("samples")[0];
            Check(review.GetProperty("surfaceDistance").GetDouble() < 1e-8, "The review view must see the point.");
            Check(application.Run(command) == 2, "Existing outputs must not be overwritten.");
            CheckForegroundProjection(store, id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void CheckForegroundProjection(IProjectStore store, string runId)
    {
        var image = store.PathFor("external.png");
        using var bitmap = new SKBitmap(100, 100);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        files.AtomicBytes(image, encoded.ToArray());
        var geometry = store.PathFor("controls.json");
        File.WriteAllText(geometry, """
            {
              "features":[{"id":"pin","geometry":"point","pixels":[[50,50]]}],
              "controlPoints":[
                {"pixel":[40,40],"position":[-0.4,-0.4,4]},
                {"pixel":[50,40],"position":[0,-0.4,4]},
                {"pixel":[60,40],"position":[0.4,-0.4,4]},
                {"pixel":[45,50],"position":[-0.2,0,4]},
                {"pixel":[55,50],"position":[0.2,0,4]},
                {"pixel":[50,60],"position":[0,0.4,4]}
              ]
            }
            """);
        var resultPath = store.PathFor("controlled.json");
        Check(application.Run(["--project", store.Root, "project-image-geometry", "--run", runId, "--image", image,
            "--focal-pixels", "100", "--geometry", geometry, "--output", resultPath]) == 0,
            "The camera should register from planar controls with a known focal length.");
        using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
        var root = document.RootElement;
        var depth = root.GetProperty("camera").GetProperty("minimumDepth").GetDouble();
        Check(depth > 2 && depth < 4, "Matched surface controls should support skipping foreground geometry.");
        var position = root.GetProperty("features")[0].GetProperty("samples")[0].GetProperty("position");
        Check(Math.Abs(position[2].GetDouble() - 4) < 1e-6, "The marked pixel should reach the controlled surface.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
