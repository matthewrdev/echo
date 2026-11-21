using Echo.Models;
using Echo.Geometry;
using Echo.Meshing;

namespace Echo.Tests;

public sealed class FinalHoleRepairTests
{
    private readonly IMeshBuilder meshes;

    public FinalHoleRepairTests(IMeshBuilder meshes)
    {
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Curved cave gaps use bounded inference without moving measured rock at different scales", () =>
        {
            foreach (var scale in new[] { .001, 1.0, 1000.0 })
            {
                var surface = CurvedRing(scale);
                var vertices = surface.Vertices.Select(point => point.ToArray()).ToArray();
                var faces = surface.Faces.Select(face => face.ToArray()).ToArray();
                var project = new ProjectData { Settings = new Settings { MeshCompleteSurface = false } };
                Check(meshes.RepairFinalHoles(project, new ReconstructionScene(), [], surface, .1 * scale, default).AddedFaces == 0,
                    "Turning off inference preserves the original conservative repair policy");
                project.Settings.MeshCompleteSurface = true;
                var repair = meshes.RepairFinalHoles(project, new ReconstructionScene(), [], surface, .1 * scale, default);
                Check(repair.RepairedHoles == 1 && repair.AddedFaces == 2,
                    "A bounded curved hole closes even when its interior has no measured depth");
                Check(surface.Vertices.Select((point, index) => point.SequenceEqual(vertices[index])).All(equal => equal)
                    && surface.Faces.Take(faces.Length).Select((face, index) => face.SequenceEqual(faces[index])).All(equal => equal),
                    "All measured vertex coordinates and triangles remain exactly fixed");
                Check(SurfaceBoundaryLoops.Find(surface, default).Count() == 1,
                    "The exterior scan boundary stays open");
            }
        });
        tests.Run("Curved inference preserves real cave openings and rejects deep folds", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            for (var index = 0; index < 3; index++)
            {
                var id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                project.Images.Add(new Photo { Id = id, Width = 100, Height = 100, FocalPixels = 100 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [(index - 1) * .1, 0, 0] });
                maps.Add(new DepthMap { ImageId = id, Width = 100, Height = 100, Depth = Enumerable.Repeat(12f, 10000).ToArray() });
            }
            Check(meshes.RepairFinalHoles(project, scene, maps, CurvedRing(1), .1, default).AddedFaces == 0,
                "Three independent views through an opening prevent a curved cap");
            var folded = CurvedRing(1);
            folded.Vertices[4][2] = 8;
            folded.Vertices[6][2] = 8;
            Check(meshes.RepairFinalHoles(project, new ReconstructionScene(), [], folded, .1, default).AddedFaces == 0,
                "A deep fold cannot be flattened into a local hole patch");
            Expect<ArgumentException>(() => SurfaceHoleRepair.Fill(CurvedRing(1), .1, default, allowCurvedInference: true));
        });
        tests.Run("Final repair closes internal gaps without moving the surface or capping the scan perimeter", () =>
        {
            var surface = Ring();
            var originalVertices = surface.Vertices.Select(point => point.ToArray()).ToArray();
            var result = meshes.RepairFinalHoles(new ProjectData(), new ReconstructionScene(), [], surface, .25, default);
            Check(result.RepairedHoles == 1 && result.AddedFaces == 2, "The enclosed square receives one complete patch");
            Check(surface.Faces.Count == 10 && SurfaceBoundaryLoops.Find(surface, default).Count() == 1,
                "Only the outer scan perimeter remains open");
            Check(surface.Vertices.Count == originalVertices.Length
                && surface.Vertices.Select((point, index) => point.SequenceEqual(originalVertices[index])).All(equal => equal),
                "Repair reuses the boundary and preserves the measured geometry");
            Check(surface.SourceImages.Count == 0, "Repair happens before texture photograph assignment");
            Check(meshes.RepairFinalHoles(new ProjectData(), new ReconstructionScene(), [], surface, .25, default).AddedFaces == 0,
                "A repeated final pass does not duplicate the cap");
        });
        tests.Run("Final repair rejects large openings and patches crossing existing geometry", () =>
        {
            var large = Ring();
            Check(meshes.RepairFinalHoles(new ProjectData(), new ReconstructionScene(), [], large, .01, default).AddedFaces == 0,
                "An opening much larger than the fusion resolution stays open");
            var crossing = Ring();
            crossing.Vertices = [.. crossing.Vertices, [0, -.4, 9], [0, .4, 9], [0, 0, 11]];
            crossing.Faces.Add([8, 9, 10]);
            Check(meshes.RepairFinalHoles(new ProjectData(), new ReconstructionScene(), [], crossing, .25, default).AddedFaces == 0,
                "The cap cannot intersect an existing folded surface");
        });
        tests.Run("Final repair preserves a small opening when multiple depth views prove it is empty space", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            foreach (var id in new[] { "a", "b", "c" })
            {
                project.Images.Add(new Photo { Id = id, Width = 100, Height = 100, FocalPixels = 100 });
                scene.Cameras.Add(new CameraPose { ImageId = id });
                maps.Add(new DepthMap { ImageId = id, Width = 100, Height = 100, Depth = Enumerable.Repeat(12f, 10000).ToArray() });
            }
            var surface = Ring();
            Check(meshes.RepairFinalHoles(project, scene, maps, surface, .25, default).AddedFaces == 0,
                "Hole repair cannot reverse a coherent free-space rejection");
            foreach (var map in maps)
            {
                Array.Fill(map.Depth, 10f);
            }
            Check(meshes.RepairFinalHoles(project, scene, maps, surface, .25, default).RepairedHoles == 1,
                "The same opening closes when measured depths agree with its surface");
        });
        tests.Run("Visibility cleanup requires spatially coherent evidence instead of drilling holes at noisy depth pixels", () =>
        {
            var photo = new Photo { Width = 100, Height = 100, FocalPixels = 100 };
            var views = new List<DepthView>();
            for (var camera = 0; camera < 3; camera++)
            {
                var map = new DepthMap { Width = 100, Height = 100, Depth = Enumerable.Repeat(10f, 10000).ToArray() };
                for (var y = 42; y <= 45; y++)
                {
                    for (var x = 49; x <= 51; x++)
                    {
                        map.Depth[y * 100 + x] = 12;
                    }
                }
                views.Add(new DepthView(photo, new CameraPose(), map));
            }
            var surface = new Surface { Vertices = [[-2, -2, 10], [2, -2, 10], [0, 2, 10]], Faces = [[0, 1, 2]] };
            Check(views.All(view => view.Observe(new V3(0, -2.0 / 3, 10), .01).Distance > 1),
                "All three depth maps disagree at the triangle centre");
            Check(SurfaceVisibilityFilter.Filter(surface, views, .01, default).Faces.Count == 1,
                "A centre-only depth spike does not delete an otherwise supported triangle");
            foreach (var view in views)
            {
                Array.Fill(view.Map.Depth, 12f);
            }
            Check(SurfaceVisibilityFilter.Filter(surface, views, .01, default).Faces.Count == 0,
                "A real sheet in front of the observed cliff is still removed");
        });
        tests.Run("Final repair validates geometry ordering and supports cancellation", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            Expect<OperationCanceledException>(() => meshes.RepairFinalHoles(project, scene, [], Ring(), .25, new CancellationToken(true)));
            Expect<ArgumentOutOfRangeException>(() => meshes.RepairFinalHoles(project, scene, [], Ring(), double.NaN, default));
            var textured = Ring();
            textured.SourceImages = Enumerable.Repeat("photo", textured.Faces.Count).ToList();
            Expect<EchoException>(() => meshes.RepairFinalHoles(project, scene, [], textured, .25, default));
        });
    }

    private Surface Ring() => new()
    {
        Vertices = [[-2, -2, 10], [2, -2, 10], [2, 2, 10], [-2, 2, 10],
            [-.5, -.5, 10], [.5, -.5, 10], [.5, .5, 10], [-.5, .5, 10]],
        Faces = [[0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
            [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]]
    };

    private Surface CurvedRing(double scale)
    {
        var surface = Ring();
        for (var index = 0; index < surface.Vertices.Count; index++)
        {
            surface.Vertices[index][2] += index % 2 == 0 ? -.3 : .3;
            for (var axis = 0; axis < 3; axis++)
            {
                surface.Vertices[index][axis] *= scale;
            }
        }
        return surface;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
