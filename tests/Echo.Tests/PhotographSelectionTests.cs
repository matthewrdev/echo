using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Meshing;

namespace Echo.Tests;

[Export]
public sealed class PhotographSelectionTests
{
    private readonly IMeshBuilder meshes;

    [ImportingConstructor]
    public PhotographSelectionTests(IMeshBuilder meshes)
    {
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Photograph detail is independent of supporting depth-map resolution", () =>
        {
            var project = new ProjectData
            {
                Images =
                [
                    new Photo { Id = "small", Width = 100, Height = 100, FocalPixels = 100 },
                    new Photo { Id = "large", Width = 400, Height = 400, FocalPixels = 400 }
                ]
            };
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "small" }, new CameraPose { ImageId = "large" }]
            };
            var maps = new[] { Map("small", 100, 10), Map("large", 100, 10) };
            var mesh = Triangle();
            var selected = meshes.AssignCameras(project, scene, maps, mesh, default);
            Check(selected.SourceImages.Single() == "large", "Same depth resolution must not hide a four-times finer photo sampling grid");

            maps[1] = Map("large", 50, 10);
            selected = meshes.AssignCameras(project, scene, maps, mesh, default);
            Check(selected.SourceImages.Single() == "large", "Reducing depth-map size cannot reduce the texture-photo score");
        });

        tests.Run("Close photographs win when they resolve more of a visible surface", () =>
        {
            var project = new ProjectData
            {
                Images = [new Photo { Id = "far", Width = 100, Height = 100, FocalPixels = 100 },
                    new Photo { Id = "close", Width = 100, Height = 100, FocalPixels = 100 }]
            };
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "far" }, new CameraPose { ImageId = "close", Centre = [0, 0, 5] }]
            };
            var maps = new[] { Map("far", 100, 10), Map("close", 100, 5) };
            var selected = meshes.AssignCameras(project, scene, maps, Triangle(), default);
            Check(selected.SourceImages.Single() == "close", "The closer photograph supplies four times as many source pixels");

            Array.Fill(maps[1].Depth, 2f);
            selected = meshes.AssignCameras(project, scene, maps, Triangle(), default);
            Check(selected.SourceImages.Single() == "far", "Higher resolution cannot override measured foreground occlusion");
        });

        tests.Run("Seam smoothing cannot replace a detailed photograph with a coarse neighbour", () =>
        {
            var candidates = new List<FaceViewCandidate>
            {
                new(0, 100, 100), new(1, 90, 81), new(2, 80, 80), new(3, 60, 25)
            };
            FaceViewSelection.RetainDetail(candidates);
            Check(candidates.Select(candidate => candidate.View).SequenceEqual([0, 1]),
                "Retain at least 90% linear resolution before graph-cut seam optimisation");
        });

        tests.Run("Removing floating islands before photo assignment releases valid source views", () =>
        {
            var project = new ProjectData { Images = [new Photo { Id = "photo", Width = 100, Height = 100, FocalPixels = 50 }] };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "photo" }] };
            var map = Map("photo", 100, 10);
            var mesh = Triangle();
            mesh.Vertices = [.. mesh.Vertices, [-.2, -.4, 5], [.2, -.4, 5], [0, 0, 5]];
            mesh.Faces.Add([3, 4, 5]);
            var views = MeshDepthViews.Build(project, scene, [map]);
            var before = FaceViewSelection.Select(mesh, views, project.Images.ToDictionary(photo => photo.Id), default);
            Check(before[0] < 0, "The floating foreground triangle really blocks the source view");
            var trimmed = SurfaceIslandTrimmer.Trim(mesh, IslandTrimMode.Main, .1, default).Surface;
            var after = meshes.AssignCameras(project, scene, [map], trimmed, default, retainUnobserved: true);
            Check(after.Faces.Count == 1 && after.SourceImages.Single() == "photo", "Assignment must use the final surviving geometry");
        });

        tests.Run("Photographic coverage reports surface area separately from triangle counts", () =>
        {
            var mesh = Triangle();
            mesh.Vertices = [.. mesh.Vertices, [-2, -2, 20], [2, -2, 20], [0, 2, 20]];
            mesh.Faces.Add([3, 4, 5]);
            mesh.SourceImages = ["", "photo"];
            var coverage = SurfacePhotographCoverage.Measure(mesh, default);
            Check(coverage.InferredFaces == 1 && coverage.Faces == 2, "Half the triangles have inferred colour");
            Check(Math.Abs(coverage.InferredAreaPercent - 20) < 1e-10, "Only one fifth of the surface area has inferred colour");
        });
    }

    private Surface Triangle() => new()
    {
        Vertices = [[-1, -1, 10], [1, -1, 10], [0, 1, 10]],
        Faces = [[0, 1, 2]]
    };

    private DepthMap Map(string id, int size, float depth) => new()
    {
        ImageId = id,
        Width = size,
        Height = size,
        Depth = Enumerable.Repeat(depth, size * size).ToArray()
    };

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
