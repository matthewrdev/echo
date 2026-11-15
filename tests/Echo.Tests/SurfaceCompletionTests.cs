using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Meshing;
using Echo.SurfaceCompletion;

namespace Echo.Tests;

[Export]
public sealed class SurfaceCompletionTests
{
    private readonly ISurfaceReconstructor reconstructor;
    private readonly IPoissonSurfaceCodec codec;
    private readonly IMeshBuilder meshes;

    [ImportingConstructor]
    public SurfaceCompletionTests(ISurfaceReconstructor reconstructor, IPoissonSurfaceCodec codec, IMeshBuilder meshes)
    {
        this.reconstructor = reconstructor;
        this.codec = codec;
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Completion samples weld texture seams and orient mixed winding toward observed cameras", () =>
        {
            var surface = new Surface
            {
                Vertices = [[0, 0, 10], [1, 0, 10], [1, 1, 10], [0, 1, 10], [0, 0, 10], [1, 1, 10]],
                Faces = [[0, 1, 2], [4, 3, 5]],
                SourceImages = ["a", "a"]
            };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
            var samples = OrientedSurfaceSamples.Build(surface, scene, default);
            Check(samples.Length == 4 && samples.All(sample => sample.Normal.Z < -.999), "Both face windings produce the same camera-facing normal after welding");
            Check(surface.Faces[0][1] == 1 && surface.Faces[1][1] == 3, "The measured input remains unchanged");
            Expect<OperationCanceledException>(() => OrientedSurfaceSamples.Build(surface, scene, new CancellationToken(true)));
            surface.SourceImages.Clear();
            Expect<EchoException>(() => OrientedSurfaceSamples.Build(surface, scene, default));
        });
        tests.Run("Gap detection preserves open scan boundaries and recognises a missing curved cap", () =>
        {
            var plane = new Surface { Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]], Faces = [[0, 1, 2], [0, 2, 3]] };
            Check(!SurfaceSupportFilter.HasGaps(plane, default), "An open plane is not an enclosed hole");
            Check(SurfaceSupportFilter.HasGaps(SphereWithMissingCap(), default), "A curved surface can have one internal hole and no outer boundary");
        });
        tests.Run("Boundary tracing follows separate face fans through a pinched vertex", () =>
        {
            var surface = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [-1, 0, 0], [0, -1, 0]],
                Faces = [[0, 1, 2], [0, 3, 4]]
            };
            var loops = SurfaceBoundaryLoops.Find(surface, default);
            Check(loops.Count == 2 && loops.All(loop => loop.Vertices.Distinct().Count() == 3),
                "Both cycles survive a shared boundary vertex");
            SurfaceHoleRepair.Fill(surface, 1, default, _ => true);
            Check(surface.Faces.Count == 2, "Tracing pinches does not cap either outer boundary");
        });
        tests.Run("Completion clipping retains nearby bridges irrespective of winding and removes distant extrapolation", () =>
        {
            var observed = new Surface { Vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0]], Faces = [[0, 1, 2]] };
            var completed = new Surface
            {
                Vertices = [[0, 0, .1], [1, 0, .1], [0, 1, .1], [10, 0, 0], [11, 0, 0], [10, 1, 0]],
                Faces = [[0, 2, 1], [3, 4, 5]]
            };
            var result = SurfaceSupportFilter.Filter(completed, observed, .2, default);
            Check(result.Faces.Count == 1 && result.Vertices.Count == 3, "Only the distant extrapolation is removed");
            Check(completed.Faces.Count == 2, "Clipping does not mutate the solved surface");
            Expect<ArgumentOutOfRangeException>(() => SurfaceSupportFilter.Filter(completed, observed, double.NaN, default));
            Expect<OperationCanceledException>(() => SurfaceSupportFilter.Filter(completed, observed, .2, new CancellationToken(true)));
        });
        tests.Run("Triangle queries distinguish shared edges, overlap, crossing and endpoint self-occlusion", () =>
        {
            var triangle = new SurfaceTriangle(new V3(0, 0, 0), new V3(2, 0, 0), new V3(0, 2, 0));
            Check((SurfaceTriangleIntersection.Closest(new V3(.5, .5, 3), triangle) - new V3(.5, .5, 0)).Length < 1e-10, "Interior projection");
            Check((SurfaceTriangleIntersection.Closest(new V3(2, 2, 0), triangle) - new V3(1, 1, 0)).Length < 1e-10, "Closest edge point");
            Check(SurfaceTriangleIntersection.Closest(new V3(-2, -1, 0), triangle).Length < 1e-10, "Closest corner");
            Check(!SurfaceTriangleIntersection.Intersects(triangle, new SurfaceTriangle(new V3(0, 0, 0), new V3(0, -2, 0), new V3(2, 0, 0))), "Shared edge is not an overlap");
            Check(SurfaceTriangleIntersection.Intersects(triangle, new SurfaceTriangle(new V3(.2, .2, 0), new V3(1, .2, 0), new V3(.2, 1, 0))), "Coplanar overlap");
            Check(SurfaceTriangleIntersection.Crosses(new V3(.5, .5, -1), new V3(.5, .5, 1), triangle), "A foreground triangle blocks a ray");
            Check(!SurfaceTriangleIntersection.Crosses(new V3(.5, .5, -1), new V3(.5, .5, 0), triangle), "A target triangle does not occlude itself");
        });
        tests.Run("Repaired regions without depth retain visible photos while foreground geometry blocks hidden faces", () =>
        {
            var project = new ProjectData { Images = [new Photo { Id = "a", Width = 100, Height = 100, FocalPixels = 50 }] };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
            var map = new DepthMap { ImageId = "a", Width = 100, Height = 100, Depth = new float[10000] };
            var surface = new Surface { Vertices = [[-1, -1, 10], [1, -1, 10], [0, 1, 10]], Faces = [[0, 1, 2]] };
            Check(meshes.AssignCameras(project, scene, [map], surface, default).Faces.Count == 1, "Missing depth alone does not delete a repaired patch");
            surface.Vertices.AddRange([[-1, -1, 5], [1, -1, 5], [0, 1, 5]]);
            surface.Faces.Add([3, 4, 5]);
            var assigned = meshes.AssignCameras(project, scene, [map], surface, default);
            Check(assigned.Faces.Count == 1 && assigned.Vertices.All(point => point[2] == 5), "The foreground patch is retained and the hidden background is rejected");
            var retained = meshes.AssignCameras(project, scene, [map], surface, default, retainUnobserved: true);
            Check(retained.Faces.Count == 2 && retained.SourceImages.Count(string.IsNullOrEmpty) == 1,
                "Completion keeps hidden geometry and marks its colour as inferred instead of assigning an occluded photograph");
        });
        tests.Run("Poisson worker output rejects invalid sizes, coordinates and indices", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-poisson-codec-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "surface.bin");
            try
            {
                foreach (var invalid in new[] { "valid", "size", "coordinate", "index" })
                {
                    using (var writer = new BinaryWriter(File.Create(path)))
                    {
                        writer.Write(0x4f504345u);
                        writer.Write(1u);
                        writer.Write(invalid == "size" ? 999999999ul : 3ul);
                        writer.Write(1ul);
                        foreach (var value in new[] { 0d, 0, invalid == "coordinate" ? double.NaN : 0, 1, 0, 0, 0, 1, 0 })
                        {
                            writer.Write(value);
                        }
                        writer.Write(0u);
                        writer.Write(1u);
                        writer.Write(invalid == "index" ? 3u : 2u);
                    }
                    if (invalid == "valid")
                    {
                        Check(codec.Read(path, default).Faces.Count == 1, "Valid worker output is accepted");
                    }
                    else
                    {
                        Expect<EchoException>(() => codec.Read(path, default));
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Bundled screened Poisson reconstructs a missing curved cap as a closed finite surface", () =>
        {
            var measured = SphereWithMissingCap();
            var scene = new ReconstructionScene();
            for (var index = 0; index < measured.Faces.Count; index++)
            {
                var face = measured.Faces[index];
                var centre = (new V3(measured.Vertices[face[0]]) + new V3(measured.Vertices[face[1]]) + new V3(measured.Vertices[face[2]])) / 3;
                var id = "camera-" + index;
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = (centre * 3).Array });
                measured.SourceImages.Add(id);
                if (index % 2 == 0)
                {
                    measured.Faces[index] = [face[0], face[2], face[1]];
                }
            }
            var completed = reconstructor.Reconstruct(measured, scene, .08, default);
            Check(completed.Faces.Count > 1000, "The actual bundled solver produced a surface");
            Check(completed.Vertices.All(point => point.All(double.IsFinite)), "All vertices are finite");
            var topology = SurfaceTopologyBuilder.Build(completed, default);
            Console.WriteLine($"  Completed sphere: {completed.Faces.Count} faces, {topology.Neighbours.Count(neighbour => neighbour < 0)} boundary edges; maximum Z {completed.Vertices.Max(point => point[2]):F5}; maximum radius error {completed.Vertices.Max(point => Math.Abs(new V3(point).Length - 1)):F5}");
            Check(topology.Neighbours.All(neighbour => neighbour >= 0), "The reconstructed sphere has no boundary holes");
            Check(completed.Vertices.Max(point => point[2]) > measured.Vertices.Max(point => point[2]) + .04, "The missing cap extends beyond the observed boundary");
            Check(completed.Vertices.All(point => Math.Abs(new V3(point).Length - 1) < .2), "Completion stays within a bounded error of the known curved shape");
            Expect<OperationCanceledException>(() => reconstructor.Reconstruct(measured, scene, .08, new CancellationToken(true)));
        });
    }

    private Surface SphereWithMissingCap()
    {
        var surface = new Surface();
        const int segments = 40;
        const int rings = 20;
        for (var ring = 4; ring < rings; ring++)
        {
            var angle = ring * Math.PI / rings;
            for (var segment = 0; segment < segments; segment++)
            {
                var longitude = segment * 2 * Math.PI / segments;
                surface.Vertices.Add([Math.Sin(angle) * Math.Cos(longitude), Math.Sin(angle) * Math.Sin(longitude), Math.Cos(angle)]);
            }
        }
        for (var ring = 0; ring < rings - 5; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                var a = ring * segments + segment;
                var b = ring * segments + next;
                surface.Faces.Add([a, a + segments, b + segments]);
                surface.Faces.Add([a, b + segments, b]);
            }
        }
        var pole = surface.Vertices.Count;
        surface.Vertices.Add([0, 0, -1]);
        for (var segment = 0; segment < segments; segment++)
        {
            surface.Faces.Add([pole - segments + segment, pole, pole - segments + (segment + 1) % segments]);
        }
        return surface;
    }

    private void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(TException).Name);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
