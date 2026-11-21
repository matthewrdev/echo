using Echo.Models;
using Echo.Geometry;
using Echo.Meshing;
using Echo.SurfaceCompletion;
using Echo.Storage;

namespace Echo.Tests;

public sealed class SurfaceCompletionTests
{
    private readonly ISurfaceReconstructor reconstructor;
    private readonly IPoissonSurfaceCodec codec;
    private readonly IMeshBuilder meshes;

    public SurfaceCompletionTests(ISurfaceReconstructor reconstructor, IPoissonSurfaceCodec codec, IMeshBuilder meshes)
    {
        this.reconstructor = reconstructor;
        this.codec = codec;
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Completion constrains large measured face interiors at the reconstruction scale", () =>
        {
            foreach (var scale in new[] { .001, 1.0, 1000.0 })
            {
                var measured = new Surface
                {
                    Vertices = [[0, 0, 10 * scale], [2 * scale, 0, 10 * scale], [0, 2 * scale, 10 * scale],
                        [3 * scale, 0, 10 * scale], [3.1 * scale, 0, 10 * scale], [3 * scale, .1 * scale, 10 * scale]],
                    Faces = [[0, 2, 1], [3, 4, 5]],
                    SourceImages = ["camera", "camera"]
                };
                var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "camera" }] };
                var originalVertices = measured.Vertices.Select(point => point.ToArray()).ToArray();
                var originalFaces = measured.Faces.Select(face => face.ToArray()).ToArray();
                var samples = OrientedSurfaceSamples.BuildRecords(measured, scene, default, interiorSampleEdgeThreshold: .4 * scale);
                Check(samples.Count == 7, "Only the coarse triangle needs an additional interior constraint");
                Check(samples.All(sample => Math.Abs(sample.Position.Z - 10 * scale) < scale * 1e-10 && sample.Normal.Z < -.999),
                    "New samples remain on the measured plane and both windings face the observing camera");
                Check(samples.Any(sample =>
                {
                    var insideX = sample.Position.X > .6 * scale && sample.Position.X < .7 * scale;
                    var insideY = sample.Position.Y > .6 * scale && sample.Position.Y < .7 * scale;
                    return insideX && insideY;
                }),
                    "The large measured face receives an interior constraint at every scene scale");
                Check(measured.Vertices.Select((point, index) => point.SequenceEqual(originalVertices[index])).All(equal => equal)
                    && measured.Faces.Select((face, index) => face.SequenceEqual(originalFaces[index])).All(equal => equal),
                    "Sampling leaves the original measured mesh exactly unchanged");
                Expect<ArgumentOutOfRangeException>(() => OrientedSurfaceSamples.BuildRecords(measured, scene, default, interiorSampleEdgeThreshold: 0));
                Expect<ArgumentOutOfRangeException>(() => OrientedSurfaceSamples.BuildRecords(measured, scene, default, interiorSampleEdgeThreshold: double.NaN));
            }
        });
        tests.Run("Completion cannot place a hull across observed free space", () =>
        {
            var fixture = new FusionFixture();
            var photo = new Photo { Id = "c", Width = 64, Height = 64, FocalPixels = 64 };
            var map = new DepthMap { ImageId = "c", Width = 64, Height = 64, Depth = Enumerable.Repeat(10f, 4096).ToArray() };
            fixture.Views.Add(new DepthView(photo, new CameraPose { ImageId = "c", Centre = [.5, 0, 0] }, map));
            var surface = new Surface
            {
                Vertices = [[-1, -1, 8], [1, -1, 8], [0, 1, 8],
                    [-1, -1, 10], [1, -1, 10], [0, 1, 10],
                    [-1, -1, 12], [1, -1, 12], [0, 1, 12]],
                Faces = [[0, 1, 2], [3, 4, 5], [6, 7, 8]],
                SourceImages = ["a", "b", ""]
            };
            var filtered = SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, default);
            Check(filtered.Faces.Count == 2 && filtered.Vertices.All(point => point[2] >= 10), "The foreground hull is rejected while measured and occluded surfaces remain");
            Check(filtered.SourceImages.SequenceEqual(new[] { "b", "" }), "Photograph assignments and inferred-colour markers follow their retained faces");
            Check(surface.Faces.Count == 3 && surface.Vertices.Count == 9, "Visibility checks preserve the input");
            Check(SurfaceVisibilityFilter.Filter(surface, fixture.Views.Take(2).ToArray(), .01, default).Faces.Count == 3,
                "Two isolated contradictory views are insufficient to erase a surface");
            for (var index = 0; index < 4; index++)
            {
                var distant = new DepthMap { ImageId = "distant-" + index, Width = 64, Height = 64, Depth = Enumerable.Repeat(98f, 4096).ToArray() };
                fixture.Views.Add(new DepthView(photo, new CameraPose { Centre = [0, 0, -90] }, distant));
            }
            Check(SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, default).Faces.Count == 2,
                "Four uncertain distant views cannot outvote three precise close views");
            foreach (var view in fixture.Views)
            {
                Array.Fill(view.Map.Depth, 0f);
            }
            Check(SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, default).Faces.Count == 3, "Unknown depths do not erase inferred patches");
            Expect<OperationCanceledException>(() => SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, new CancellationToken(true)));
        });
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
            var loops = SurfaceBoundaryLoops.Find(surface, default).ToList();
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
            surface.Vertices = [.. surface.Vertices, [-1, -1, 5], [1, -1, 5], [0, 1, 5]];
            surface.Faces.Add([3, 4, 5]);
            var assigned = meshes.AssignCameras(project, scene, [map], surface, default);
            Check(assigned.Faces.Count == 1 && assigned.Vertices.All(point => point[2] == 5), "The foreground patch is retained and the hidden background is rejected");
            var retained = meshes.AssignCameras(project, scene, [map], surface, default, retainUnobserved: true);
            Check(retained.Faces.Count == 2 && retained.SourceImages.Count(string.IsNullOrEmpty) == 1,
                "Completion keeps hidden geometry and marks its colour as inferred instead of assigning an occluded photograph");
            Array.Fill(map.Depth, 10f);
            foreach (var vertex in surface.Vertices.Skip(3))
            {
                vertex[2] = 9.8;
            }
            var overlappingDepth = meshes.AssignCameras(project, scene, [map], surface, default, retainUnobserved: true);
            Check(overlappingDepth.SourceImages.Count(string.IsNullOrEmpty) == 1,
                "Depth tolerance cannot make a photograph see through a foreground triangle");
        });
        tests.Run("Poisson worker output rejects invalid sizes, coordinates and indices", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-poisson-codec-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "surface.bin");
            try
            {
                foreach (var invalid in new[] { "valid", "size", "coordinate", "index", "duplicate", "unsigned" })
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
                        writer.Write(invalid == "index" ? 3u : invalid == "duplicate" ? 1u : invalid == "unsigned" ? uint.MaxValue : 2u);
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
        tests.Run("Poisson binary blocks preserve coordinates, sample bytes and tail records", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "echo-poisson-blocks-" + Guid.NewGuid().ToString("N"));
            try
            {
                const int vertexCount = 6001;
                const int faceCount = 6007;
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(0x4f504345u);
                    writer.Write(1u);
                    writer.Write((ulong)vertexCount);
                    writer.Write((ulong)faceCount);
                    for (var index = 0; index < vertexCount; index++)
                    {
                        writer.Write(index * Math.PI);
                        writer.Write(-0d);
                        writer.Write(double.Epsilon);
                    }
                    for (var index = 0; index < faceCount; index++)
                    {
                        writer.Write(index % vertexCount);
                        writer.Write((index + 1) % vertexCount);
                        writer.Write((index + 2) % vertexCount);
                    }
                }
                var surface = codec.Read(path, default);
                var scratchDirectory = path + "-scratch";
                try
                {
                    using var session = new BulkDataSession(65536 * 2, 0);
                    using var work = new WorkBuffers(session, scratchDirectory);
                    var paged = codec.Read(path, default, work);
                    Check(paged.Vertices is PagedList<double[]> && paged.Faces is PagedList<int[]>, "Poisson output escaped the paging budget");
                    for (var vertex = 0; vertex < surface.Vertices.Count; vertex++)
                    {
                        Check(paged.Vertices[vertex].Select(BitConverter.DoubleToInt64Bits)
                            .SequenceEqual(surface.Vertices[vertex].Select(BitConverter.DoubleToInt64Bits)), "Paged Poisson vertex bits differ");
                    }
                    for (var face = 0; face < surface.Faces.Count; face++)
                    {
                        Check(paged.Faces[face].SequenceEqual(surface.Faces[face]), "Paged Poisson triangles differ");
                    }
                }
                finally
                {
                    Directory.Delete(scratchDirectory, true);
                }
                Check(surface.Vertices.Count == vertexCount && surface.Faces.Count == faceCount, "Block tail lost records");
                for (var index = 0; index < vertexCount; index++)
                {
                    var point = surface.Vertices[index];
                    Check(point[0] == index * Math.PI && BitConverter.DoubleToInt64Bits(point[1]) == long.MinValue
                        && point[2] == double.Epsilon, "Vertex precision or signed zero changed");
                }
                for (var index = 0; index < faceCount; index++)
                {
                    Check(surface.Faces[index].SequenceEqual(new[] { index % vertexCount, (index + 1) % vertexCount, (index + 2) % vertexCount }), "Face order or winding changed");
                }
                Expect<OperationCanceledException>(() => codec.Read(path, new CancellationToken(true)));
                using (var stream = File.OpenWrite(path))
                {
                    stream.SetLength(stream.Length - 1);
                }
                Expect<EchoException>(() => codec.Read(path, default));

                var samples = Enumerable.Range(0, 3001).Select(index => new OrientedSurfaceSample(
                    new V3(index, -0d, double.Epsilon), new V3(.25, -.5, Math.PI))).ToArray();
                codec.WriteSamples(path, samples, default);
                using var expected = new MemoryStream();
                using (var writer = new BinaryWriter(expected, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(0x49504345u);
                    writer.Write(1u);
                    writer.Write((ulong)samples.Length);
                    foreach (var sample in samples)
                    {
                        writer.Write(sample.Position.X);
                        writer.Write(sample.Position.Y);
                        writer.Write(sample.Position.Z);
                        writer.Write(sample.Normal.X);
                        writer.Write(sample.Normal.Y);
                        writer.Write(sample.Normal.Z);
                    }
                }
                Check(File.ReadAllBytes(path).SequenceEqual(expected.ToArray()), "Buffered sample writes changed the worker contract");
                Expect<OperationCanceledException>(() => codec.WriteSamples(path, samples, new CancellationToken(true)));
            }
            finally
            {
                File.Delete(path);
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
