using Echo.Cli;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

public sealed class StagePreviewTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IFileOperations files;
    private readonly ICommandApplication cli;

    public StagePreviewTests(IProjectStoreFactory stores, IFileOperations files, ICommandApplication cli)
    {
        this.stores = stores;
        this.files = files;
        this.cli = cli;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Camera lookup and alignment preview use the recovered pose", CheckCamera);
        tests.Run("Fusion preview renders signed voxels at the requested coordinate and preserves existing output", CheckFusion);
    }

    private void CheckCamera()
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-camera-preview-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Camera preview test");
        try
        {
            const string imageId = "image-a";
            const string artifact = "artifacts/alignment/test";
            var run = new RunData
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "completed",
                Snapshot = new ProjectData
                {
                    Images = [new Photo { Id = imageId, Name = "photo.jpg", Width = 100, Height = 80, FocalPixels = 60 }]
                },
                Artifacts = new Dictionary<string, string> { ["alignment"] = artifact }
            };
            store.SaveRun(run);
            files.AtomicData(store.PathFor(artifact + "/scene.json"), new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = imageId, FocalPixels = 50 }],
                Points =
                [
                    new CloudPoint { Position = [0, 0, 2], Colour = [255, 0, 0] },
                    new CloudPoint { Position = [0, 0, 4], Colour = [0, 255, 0] },
                    new CloudPoint { Position = [0, 0, -1], Colour = [0, 0, 255] }
                ]
            });

            var capture = new StringWriter();
            var previous = Console.Out;
            try
            {
                Console.SetOut(capture);
                Check(cli.Run(["--project", root, "preview", "cameras", "alignment", "--run", run.Id,
                    "--camera", imageId]) == 0, "Camera lookup succeeds");
            }
            finally
            {
                Console.SetOut(previous);
            }

            using var result = System.Text.Json.JsonDocument.Parse(capture.ToString());
            var entry = result.RootElement.GetProperty("cameras")[0];
            Check(entry.GetProperty("name").GetString() == "photo.jpg", "Photo ID resolves to the snapshot name");
            Check(entry.GetProperty("pose").GetProperty("focalPixels").GetDouble() == 50, "Recovered focal length");

            var output = store.PathFor("camera.png");
            Check(cli.Run(["--project", root, "preview", "alignment", "--run", run.Id,
                "--camera", imageId, "--width", "100", "--output", output]) == 0, "Camera preview succeeds");
            using var png = SKBitmap.Decode(output);
            Check(png is { Width: 100, Height: 80 }, "Preview uses the source photo aspect");
            Check(png!.GetPixel(50, 40).Red == 255, "Nearest visible point wins");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void CheckFusion()
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-stage-preview-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init("Stage preview test");
        try
        {
            var runId = Guid.NewGuid().ToString("N");
            const string artifact = "artifacts/fusion/test";
            const string image = "slice.png";
            const string missing = "missing.png";
            files.AtomicData(store.PathFor(artifact + "/volume.json"), new FusedVolume
            {
                VoxelSize = 1,
                Samples =
                [
                    new VolumeSample { X = 0, Y = 0, Z = -2, Distance = -3, Views = 2 },
                    new VolumeSample { X = 1, Y = 0, Z = -2, Distance = 3, Views = 2 },
                    new VolumeSample { X = 0, Y = 0, Z = 0, Distance = 0, Views = 2 }
                ]
            });
            store.SaveRun(new RunData
            {
                Id = runId,
                Status = "completed",
                Artifacts = new Dictionary<string, string> { ["fusion"] = artifact }
            });

            Check(Preview(root, runId, "-2", store.PathFor(image)) == 0, "Preview succeeds");
            using var png = SKBitmap.Decode(store.PathFor(image));
            Check(png is { Width: 100, Height: 80 }, "Preview size");
            Check(png!.GetPixel(25, 40).Blue > 200, "Negative distance is blue");
            Check(png.GetPixel(75, 40).Red > 200, "Positive distance is orange");

            var bytes = File.ReadAllBytes(store.PathFor(image));
            Check(Preview(root, runId, "0", store.PathFor(image)) == 2, "Existing path rejected");
            Check(bytes.SequenceEqual(File.ReadAllBytes(store.PathFor(image))), "Existing output preserved");
            Check(Preview(root, runId, "99", store.PathFor(missing)) == 2, "Missing slice rejected");
            Check(!File.Exists(store.PathFor(missing)), "Failed preview leaves no output");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private int Preview(string root, string runId, string slice, string output)
    {
        return cli.Run(["project", root, "preview", "fusion", "--run", runId,
            "--slice", slice, "--width", "100", "--height", "80", "--output", output]);
    }

    private void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }
}
