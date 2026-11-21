using Echo.Models;
using Echo.Depth;
using Echo.Geometry;

namespace Echo.Tests;

public sealed class DepthNeighbourTests
{
    public DepthNeighbourTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Stereo overlap includes a camera twenty metres lower with opposing pitch", () =>
        {
            var reference = new CameraPose { ImageId = "reference", Centre = [0, -10, 0], Rotation = Tilt(-30) };
            var lower = new CameraPose { ImageId = "lower", Centre = [0, 10, 0], Rotation = Tilt(30) };
            var scene = new ReconstructionScene
            {
                Cameras =
                [
                    reference,
                    new CameraPose { ImageId = "near1", Centre = [1, -10, 0], Rotation = Tilt(-30) },
                    new CameraPose { ImageId = "near2", Centre = [-1.2, -10, 0], Rotation = Tilt(-30) },
                    lower
                ],
                Points = [new CloudPoint { Position = [0, 0, 30], ImageA = "reference", ImageB = "lower" }]
            };
            var project = Project(scene);
            var point = new V3(0, 0, 30);
            Check(Support(project, [reference, lower], point) == 2, "Opposing camera tilts still see the same rock");
            var range = new DepthSearchRange(25, 35, 30, 1, "intersecting vertical views");
            var selected = DepthNeighbourSelector.Select(project, scene, reference, range, 3, default);
            Check(selected.Contains(lower), "A sixty-degree orientation difference must not exclude real surface overlap");
        });
        tests.Run("Stereo selection expands beyond nearby cameras to cover both image edges twice", () =>
        {
            var scene = Scene();
            var project = Project(scene);
            var selected = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 3, default);
            Check(selected.Length >= 4, "Three neighbours on one side cannot supply two observations of both edges");
            Check(Support(project, selected, new V3(-5, 0, 10)) >= 2, "The left edge needs two covering views");
            Check(Support(project, selected, new V3(5, 0, 10)) >= 2, "The right edge needs two covering views");
            Check(selected.Length <= DepthNeighbourSelector.MaximumNeighbours(3, scene.Cameras.Count), "Respect the source budget");
            var repeated = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 3, default);
            Check(selected.SequenceEqual(repeated), "Selection is deterministic");
        });
        tests.Run("Adaptive additions exclude redundant positions and cameras facing away from the surface", () =>
        {
            var scene = Scene();
            scene.Cameras.Add(new CameraPose { ImageId = "duplicate", Centre = [1.00001, 0, 0] });
            scene.Cameras.Add(new CameraPose { ImageId = "away", Centre = [.1, 0, 0], Rotation = [-1, 0, 0, 0, 1, 0, 0, 0, -1] });
            var project = Project(scene);
            var selected = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 1, default);
            Check(selected.All(camera => camera.ImageId != "away"), "Facing direction alone does not establish overlap");
            Check(selected.Count(camera => Math.Abs(camera.Centre[0] - 1) < .01) == 1,
                "Near-identical positions must not occupy two support slots");
        });
        tests.Run("Measured depth refresh adds missing coverage without evicting already loaded sources", () =>
        {
            var scene = Scene();
            var project = Project(scene);
            var prior = new DepthMap
            {
                Width = 160,
                Height = 120,
                Depth = Enumerable.Repeat(10f, 160 * 120).ToArray()
            };
            CameraPose[] previous = [scene.Cameras[1], scene.Cameras[2], scene.Cameras[3]];
            var range = new DepthSearchRange(8, 120, 100, 0, "uncertain range");
            var selected = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], range, 3, default, prior, previous);
            Check(previous.All(selected.Contains), "Keep the source lifetime union bounded by retaining existing slots");
            Check(Support(project, selected, new V3(-5, 0, 10)) >= 2, "Measured near geometry reveals missing left coverage");
            Check(selected.Length <= DepthNeighbourSelector.MaximumNeighbours(3, scene.Cameras.Count), "Refinement stays within admission");
        });
        tests.Run("Coincident nearby photographs do not satisfy two-view coverage", () =>
        {
            var scene = Scene();
            scene.Cameras.Add(new CameraPose { ImageId = "duplicate", Centre = [1.00001, 0, 0] });
            var selected = DepthNeighbourSelector.Select(Project(scene), scene, scene.Cameras[0], Range(), 2, default);
            Check(selected.Any(camera => camera.ImageId == "duplicate"), "Preserve the configured nearby photographic matches");
            Check(selected.Any(camera => camera.ImageId is "right2" or "right3"),
                "Another independent position is required even when two nearby photos are retained");
            Check(selected.Count(camera => camera.Centre[0] < 0) >= 2, "Both left-edge observations must also be supplied");
        });
        tests.Run("Failed coarse matches recruit additional viewpoints during refinement", () =>
        {
            var scene = Scene();
            scene.Cameras.Add(new CameraPose { ImageId = "right4", Centre = [1.6, 0, 0] });
            scene.Cameras.Add(new CameraPose { ImageId = "left3", Centre = [-2, 0, 0] });
            scene.Cameras.Add(new CameraPose { ImageId = "left4", Centre = [-2.2, 0, 0] });
            var project = Project(scene);
            var initial = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 3, default);
            var prior = new DepthMap { Width = 320, Height = 240, Depth = new float[320 * 240] };
            var refined = DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 3, default, prior, initial);
            Check(refined.Length > initial.Length, "Predicted overlap must not stop recruitment when matching still fails");
            Check(initial.All(refined.Contains), "Refinement preserves nearby photographic matches");
            Check(Support(project, refined, new V3(-5, 0, 10)) >= 4, "The failed left edge gets extra independent matching opportunities");
        });
        tests.Run("Stereo selection needs real parallax and reports no usable view instead of inventing support", () =>
        {
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "reference" }, new CameraPose { ImageId = "tiny", Centre = [.06, 0, 0] }]
            };
            var project = Project(scene);
            var range = new DepthSearchRange(900, 1100, 1000, 0, "distant surface");
            Check(DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], range, 3, default).Length == 0,
                "A tiny baseline cannot triangulate a distant surface");
            scene.Cameras.Add(new CameraPose { ImageId = "usable", Centre = [20, 0, 0] });
            project = Project(scene);
            Check(DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], range, 3, default).Single().ImageId == "usable",
                "A single genuinely usable neighbour is retained when no second view exists");
        });
        tests.Run("Adaptive stereo selection remains bounded and honours cancellation", () =>
        {
            var scene = Scene();
            var project = Project(scene);
            Check(DepthNeighbourSelector.MaximumNeighbours(3, 1000) == 12, "Default adaptive source cap");
            Check(DepthNeighbourSelector.MaximumNeighbours(24, 1000) == 24, "Higher configured minimum reserves more sources");
            Check(DepthNeighbourSelector.MaximumNeighbours(3, 2) == 1, "Small scenes reserve only existing neighbours");
            Check(DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 0, default).Length == 0, "Empty request");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                DepthNeighbourSelector.Select(project, scene, scene.Cameras[0], Range(), 3, cancellation.Token);
                throw new InvalidOperationException("Selection ignored cancellation");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private ReconstructionScene Scene()
    {
        return new ReconstructionScene
        {
            Cameras =
            [
                new CameraPose { ImageId = "reference" },
                new CameraPose { ImageId = "right1", Centre = [1, 0, 0] },
                new CameraPose { ImageId = "right2", Centre = [1.2, 0, 0] },
                new CameraPose { ImageId = "right3", Centre = [1.4, 0, 0] },
                new CameraPose { ImageId = "left1", Centre = [-1.6, 0, 0] },
                new CameraPose { ImageId = "left2", Centre = [-1.8, 0, 0] }
            ]
        };
    }

    private ProjectData Project(ReconstructionScene scene)
    {
        return new ProjectData
        {
            Settings = new Settings { DepthWidth = 160 },
            Images = scene.Cameras.Select(camera => new Photo
            {
                Id = camera.ImageId,
                Width = 160,
                Height = 120,
                FocalPixels = 150
            }).ToList()
        };
    }

    private DepthSearchRange Range() => new(8, 12, 10, 0, "test surface");

    private double[] Tilt(double degrees)
    {
        var angle = degrees * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return [1, 0, 0, 0, cosine, -sine, 0, sine, cosine];
    }

    private int Support(ProjectData project, CameraPose[] cameras, V3 point)
    {
        return cameras.Count(camera =>
        {
            var photo = project.Images.Single(photo => photo.Id == camera.ImageId);
            var pixel = CameraGeometry.ProjectPoint(photo, camera, point);
            if (!pixel.HasValue)
            {
                return false;
            }
            var p = pixel.Value;
            var inside = p.X >= 3 && p.X < photo.Width - 3 && p.Y >= 3 && p.Y < photo.Height - 3;
            return inside;
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
