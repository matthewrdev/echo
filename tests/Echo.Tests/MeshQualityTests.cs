using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Simplification;

namespace Echo.Tests;

[Export]
public sealed class MeshQualityTests
{
    private readonly IMeshBuilder meshes;
    private readonly IEdgeCollapse simplifier;

    [ImportingConstructor]
    public MeshQualityTests(IMeshBuilder meshes, IEdgeCollapse simplifier)
    {
        this.meshes = meshes;
        this.simplifier = simplifier;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Parallel collapse joins seams without holes and is deterministic across worker counts", () =>
        {
            var source = DetailSurface();
            var serial = ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, 200, default, .01, null, null, null, 4, 1);
            var parallel = ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, 200, default, .01, null, null, null, 4, 4);
            var baseline = simplifier.Reduce(source, 200, default, .01);
            Console.WriteLine($"  Region reduction: {parallel.Faces.Count} faces, peak {parallel.Vertices.Max(point => point[2]):F6}; serial baseline {baseline.Faces.Count} faces, peak {baseline.Vertices.Max(point => point[2]):F6}");
            Check(serial.Faces.Count == parallel.Faces.Count && serial.Vertices.Count == parallel.Vertices.Count, "Worker count changed mesh size");
            Check(serial.Vertices.Zip(parallel.Vertices).All(pair => pair.First.SequenceEqual(pair.Second))
                && serial.Faces.Zip(parallel.Faces).All(pair => pair.First.SequenceEqual(pair.Second)), "Scheduling changed the deterministic mesh");
            Check(EulerCharacteristic(parallel) == 1 && parallel.Faces.All(face => Normal(parallel, face).Z > 0), "Region joining introduced holes or inverted faces");
            Check(parallel.Faces.Count <= 200 && parallel.Vertices.Max(point => point[2]) > .7, "Parallel reduction missed the ceiling or removed high-relief detail");
            Check(source.Faces.Count == 1200, "Parallel simplification mutated its source");
        });
        tests.Run("Parallel collapse preserves protected vertices and supports cancellation", () =>
        {
            var source = DetailSurface();
            var tolerances = source.Vertices.Select(point => point[0] < 8 ? 0d : .1).ToArray();
            var reduced = ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, source.Faces.Count, default, 0,
                tolerances, null, null, 4, 4);
            foreach (var point in source.Vertices.Where(point => point[0] < 8))
            {
                Check(reduced.Vertices.Any(vertex => vertex.SequenceEqual(point)), "Parallel collapse moved a protected vertex");
            }
            Check(EulerCharacteristic(reduced) == 1, "Protected material boundaries developed holes");
            using var cancellation = new CancellationTokenSource();
            ExpectCancellation(() => ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, 200, cancellation.Token, .01,
                null, null, update =>
                {
                    if (update.Operation == "Parallel edge collapse")
                    {
                        cancellation.Cancel();
                    }
                }, 4, 4));
        });
        tests.Run("Triangle search index agrees with direct geometry queries", () =>
        {
            var mesh = new Surface();
            var triangles = new List<SurfaceTriangle>();
            var random = new Random(818);
            for (var index = 0; index < 250; index++)
            {
                var start = mesh.Vertices.Count;
                var x = random.NextDouble() * 10;
                var y = random.NextDouble() * 10;
                var z = random.NextDouble();
                mesh.Vertices.Add([x, y, z]);
                mesh.Vertices.Add([x + .4, y, z + .1]);
                mesh.Vertices.Add([x, y + .4, z - .1]);
                mesh.Faces.Add([start, start + 1, start + 2]);
                triangles.Add(new SurfaceTriangle(new V3(mesh.Vertices[start]), new V3(mesh.Vertices[start + 1]), new V3(mesh.Vertices[start + 2])));
            }
            using var search = new SurfaceTriangleIndex(mesh, default);
            for (var query = 0; query < 100; query++)
            {
                var point = new V3(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble());
                var expected = triangles.Select(triangle => SurfaceTriangleIntersection.Closest(point, triangle))
                    .Min(position => (point - position).Dot(point - position));
                Check(Math.Abs(search.Nearest(point, 100).DistanceSquared - expected) < 1e-12, "Indexed nearest surface changed");
                var end = point + new V3(0, 0, -2);
                Check(search.HasOccluder(point, end) == triangles.Any(triangle => SurfaceTriangleIntersection.Crosses(point, end, triangle)),
                    "Indexed visibility changed");
                var patch = new SurfaceTriangle(point, point + new V3(.5, 0, 0), point + new V3(0, .5, .5));
                Check(search.Intersects(patch) == triangles.Any(triangle => SurfaceTriangleIntersection.Intersects(patch, triangle)),
                    "Indexed triangle intersection changed");
            }
        });
        tests.Run("Hole repair reports bounded updates and the final boundary count", () =>
        {
            var mesh = new Surface();
            const int count = 1000;
            for (var index = 0; index < count; index++)
            {
                var start = mesh.Vertices.Count;
                mesh.Vertices.Add([index * 10, 0, 0]);
                mesh.Vertices.Add([index * 10 + 1, 0, 0]);
                mesh.Vertices.Add([index * 10, 1, 0]);
                mesh.Faces.Add([start, start + 1, start + 2]);
            }
            var updates = new List<OperationProgress>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SurfaceHoleRepair.Fill(mesh, 1, default, progress: updates.Add);
            var repair = updates.Where(update => update.Operation == "Hole repair").ToArray();
            Check(repair.Last().Completed == count && repair.Last().Total == count, "Final examined boundaries were not reported");
            Check(repair.Length <= 2 + Math.Ceiling(clock.Elapsed.TotalSeconds * 8), "Boundary processing emitted per-item progress spam");
            Check(mesh.Faces.Count == count, "Outer boundaries were incorrectly filled");
        });
        tests.Run("Simplification reports preparation and compaction with real totals", () =>
        {
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                Faces = [[0, 1, 2], [0, 2, 3]]
            };
            var updates = new List<OperationProgress>();
            var reduced = simplifier.Reduce(mesh, 1, default, progress: updates.Add);
            Check(updates.Select(update => update.StepIndex).Distinct().SequenceEqual(Enumerable.Range(0, 8).Select(index => (int?)index)),
                "Preparation, quadrics, edges, constraints, ranking, collapse and output must be visible in order");
            Check(updates.All(update => update.Steps?.Length == 8), "Substage progress needs its complete plan");
            Check(updates.First().Completed == 0 && updates.First().Total == 4, "Vertex preparation has a known total");
            Check(updates.Any(update => update.Operation == "Collapse edges" && update.Total == null),
                "A changing candidate queue must not pretend to have a fixed total");
            Check(updates.Last().Completed == 2 && updates.Last().Total == 2, "Compaction must report completion");
            Check(updates.Last().Message.Contains($"{reduced.Faces.Count:N0} triangles remain", StringComparison.Ordinal), "The final face count was lost");
        });
        tests.Run("Simplification can be cancelled during candidate preparation", () =>
        {
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                Faces = [[0, 1, 2], [0, 2, 3]]
            };
            using var cancellation = new CancellationTokenSource();
            try
            {
                simplifier.Reduce(mesh, 1, cancellation.Token, progress: update =>
                {
                    if (update.Operation == "Rank collapse candidates")
                    {
                        cancellation.Cancel();
                    }
                });
                throw new InvalidOperationException("Cancellation was ignored during preparation");
            }
            catch (OperationCanceledException)
            {
                Check(mesh.Faces.Count == 2 && mesh.Vertices.Count == 4, "Cancellation mutated the saved source");
            }
        });
        tests.Run("Irregular holes requiring observations remain open until a support check is available", () =>
        {
            var height = Math.Sqrt(3) * 11.5 / 2;
            foreach (var support in new Func<V3, bool>?[] { null, _ => false, _ => true })
            {
                var mesh = new Surface
                {
                    Vertices = [[0, 0, 0], [11.5, 0, 0], [5.75, height, 0],
                        [-5.75, -height / 3, 0], [17.25, -height / 3, 0], [5.75, height * 5 / 3, 0]],
                    Faces = [[0, 3, 4], [0, 4, 1], [1, 4, 5], [1, 5, 2], [2, 5, 3], [2, 3, 0]]
                };
                // Every pair of inner vertices is within the 12-voxel limit,
                // but their radius about the mean exceeds 6 voxels.
                var originalArea = Area(mesh);
                var accepted = support?.Invoke(default) == true;
                SurfaceHoleRepair.Fill(mesh, 1, default, support);
                Check(EulerCharacteristic(mesh) == (accepted ? 1 : 0),
                    "The opening closes only when observed support is supplied and agrees");
                Check(Math.Abs(Area(mesh) - originalArea - (accepted ? 11.5 * height / 2 : 0)) < 1e-8,
                    "Missing or rejected observations preserve the opening and surrounding surface");
            }
        });
        tests.Run("Ground stitching bridges supported fragments with a manifold patch and bounded extent", () =>
        {
            foreach (var supported in new[] { true, false })
            {
                var mesh = new Surface
                {
                    Vertices = [[0, 0, 0], [1, 0, 0], [1, 0, 1], [0, 0, 1],
                        [3, 0, 0], [4, 0, 0], [4, 0, 1], [3, 0, 1]],
                    Faces = [[0, 2, 1], [0, 3, 2], [4, 6, 5], [4, 7, 6]]
                };
                GroundSurfaceCompletion.Fill(mesh, .5, _ => supported, default);
                Check(EulerCharacteristic(mesh) == (supported ? 1 : 2), "The bridge connects two patches without pinched or non-manifold edges");
                Check(Math.Abs(Area(mesh) - (supported ? 4 : 2)) < 1e-8, "Only the observed gap is filled; the footprint does not grow");
                Check(mesh.Vertices.Count == 8 && mesh.Faces.All(face => Normal(mesh, face).Y > 0),
                    "Ground completion uses existing boundary vertices and consistent upward winding");
            }
        });
        tests.Run("Ground stitching does not overlap a reverse-wound surface already inside a gap", () =>
        {
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [1, 0, 1], [0, 0, 1],
                    [3, 0, 0], [4, 0, 0], [4, 0, 1], [3, 0, 1],
                    [1.5, 0, .25], [2.5, 0, .25], [2.5, 0, .75], [1.5, 0, .75]],
                Faces = [[0, 2, 1], [0, 3, 2], [4, 6, 5], [4, 7, 6], [8, 9, 10], [8, 10, 11]]
            };
            GroundSurfaceCompletion.Fill(mesh, .5, _ => true, default);
            Check(mesh.Faces.Count == 6, "Existing surface blocks overlapping bridges regardless of winding");
        });
        tests.Run("Texture graph cuts match exhaustive binary energies and support long paths", () =>
        {
            var random = new Random(721);
            var cut = new BinaryCut(15);
            for (var sample = 0; sample < 40; sample++)
            {
                var count = 3 + sample % 5;
                cut.Reset(count);
                var keep = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 3 - 1).ToArray();
                var change = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 3 - 1).ToArray();
                var weights = new double[count, count];
                for (var node = 0; node < count; node++)
                {
                    cut.Unary(node, keep[node], change[node]);
                    for (var other = node + 1; other < count; other++)
                    {
                        weights[node, other] = random.NextDouble();
                        cut.Pair(node, other, weights[node, other], weights[node, other]);
                    }
                }
                double Energy(bool[] labels)
                {
                    var value = 0.0;
                    for (var node = 0; node < count; node++)
                    {
                        value += labels[node] ? change[node] : keep[node];
                        for (var other = node + 1; other < count; other++)
                        {
                            value += labels[node] == labels[other] ? 0 : weights[node, other];
                        }
                    }
                    return value;
                }
                var minimum = Enumerable.Range(0, 1 << count).Min(mask => Energy(Enumerable.Range(0, count).Select(bit => (mask & (1 << bit)) != 0).ToArray()));
                var labels = new bool[count];
                cut.Solve(labels, default);
                Check(Math.Abs(Energy(labels) - minimum) < 1e-8, "The cut gives the globally minimal binary energy");
            }
            var chain = new BinaryCut(20000);
            chain.Unary(0, 0, 2);
            chain.Unary(19999, 2, 0);
            for (var node = 0; node < 19999; node++)
            {
                chain.Pair(node, node + 1, 1, 1);
            }
            var result = new bool[20000];
            chain.Solve(result, default);
            Check(!result[0] && result[^1], "Long augmenting paths do not require recursive stack space");
        });
        tests.Run("Tetrahedral extraction resolves saddle cells and welds exact grid crossings", () =>
        {
            foreach (var saddle in new[] { false, true })
            {
                var volume = PlaneVolume();
                for (var index = 0; index < volume.Samples.Count; index++)
                {
                    var sample = volume.Samples[index];
                    sample.Distance = saddle ? sample.Z + ((sample.X + sample.Y) % 2 == 0 ? .8f : -.8f) - .25f : sample.Z;
                    volume.Samples[index] = sample;
                }
                var mesh = meshes.Extract(volume, default);
                Check(EulerCharacteristic(mesh) == 1, "Ambiguous cell faces must not create non-manifold edges or holes");
                Check(mesh.Faces.All(face => Normal(mesh, face).Length > 1e-10), "Exact grid crossings do not create degenerate triangles");
            }
        });
        tests.Run("Meshing repairs isolated depth gaps and enclosed holes without closing the outer boundary", () =>
        {
            foreach (var radius in new[] { 0, 1, 2 })
            {
                var volume = PlaneVolume();
                ((List<VolumeSample>)volume.Samples).RemoveAll(s => Math.Abs(s.X - 12) <= radius && Math.Abs(s.Y - 10) <= radius);
                var originalCount = volume.Samples.Count;
                var mesh = meshes.Extract(volume, default);
                Check(volume.Samples.Count == originalCount, "Repair must not mutate the persisted fusion input");
                Check(Math.Abs(Area(mesh) - 30 * 20) < 1e-6, "The small hole is filled without expanding the plane");
                Check(EulerCharacteristic(mesh) == 1, "The repaired plane is a disk");
                Check(mesh.Vertices.All(p => Math.Abs(p[2] - .25) < 1e-6), "Repairs follow the observed surface");
                Check(mesh.Faces.All(f => Normal(mesh, f).Z > 0), "Filled faces preserve winding and do not fold");
            }
        });
        tests.Run("Meshing leaves large unsupported gaps and open scan edges intact", () =>
        {
            var volume = PlaneVolume();
            ((List<VolumeSample>)volume.Samples).RemoveAll(s => s.X >= 10 && s.X <= 19 && s.Y >= 6 && s.Y <= 15);
            var mesh = meshes.Extract(volume, default);
            Check(EulerCharacteristic(mesh) == 0, "A large internal opening remains an opening");
            Check(Area(mesh) < 30 * 20 - 80, "Large missing areas are not fabricated");

            volume = PlaneVolume();
            ((List<VolumeSample>)volume.Samples).RemoveAll(s => s.X <= 2 && Math.Abs(s.Y - 10) <= 1);
            mesh = meshes.Extract(volume, default);
            Check(Area(mesh) < 30 * 20 - 3, "A notch connected to the outer edge is not capped");
        });
        tests.Run("Meshing removes tiny detached fragments without deleting the main surface", () =>
        {
            var volume = PlaneVolume();
            for (var x = 100; x <= 102; x++)
            {
                for (var y = 100; y <= 102; y++)
                {
                    for (var z = 0; z <= 2; z++)
                    {
                        var radius = new V3(x - 101, y - 101, z - 1).Length;
                        volume.Samples.Add(new VolumeSample { X = x, Y = y, Z = z, Distance = (float)(radius - .25), Views = 2 });
                    }
                }
            }
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.All(point => point[0] <= 30), "Sub-voxel floating fragments are removed");
            Check(Math.Abs(Area(mesh) - 600) < 1e-6, "The main surface is unchanged");
        });
        tests.Run("Larger surface holes close only when depth observations support the patch interior", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            foreach (var id in new[] { "a", "b" })
            {
                project.Images.Add(new Photo { Id = id, Width = 100, Height = 100, FocalPixels = 40 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [15, 10, -30] });
                maps.Add(new DepthMap { ImageId = id, Width = 100, Height = 100, Depth = Enumerable.Repeat(30.25f, 10000).ToArray() });
            }
            foreach (var supported in new[] { true, false })
            {
                var volume = PlaneVolume();
                ((List<VolumeSample>)volume.Samples).RemoveAll(s => s.X >= 10 && s.X <= 19 && s.Y >= 6 && s.Y <= 15);
                var mesh = meshes.Extract(volume, default);
                if (!supported)
                {
                    foreach (var map in maps)
                    {
                        Array.Fill(map.Depth, 20f);
                    }
                }
                meshes.FillHoles(project, scene, maps, mesh, 1, default);
                Check(EulerCharacteristic(mesh) == (supported ? 1 : 0), "Only a patch with agreeing measured depths closes the hole");
                if (supported)
                {
                    Check(Math.Abs(Area(mesh) - 600) < 1e-6, "The completed plane retains its exact area and outer boundary");
                }
            }
        });
        tests.Run("Adaptive simplification spends faces on curved regions even below the face ceiling", () =>
        {
            var source = DetailSurface();
            var reduced = simplifier.Reduce(source, 10000, default, .1);
            var flatFaces = reduced.Faces.Count(f => f.Average(v => reduced.Vertices[v][0]) < 12);
            var curvedFaces = reduced.Faces.Count(f => f.Average(v => reduced.Vertices[v][0]) > 18);
            Console.WriteLine($"  Adaptive mesh: {source.Faces.Count} → {reduced.Faces.Count} faces; flat {flatFaces}, curved {curvedFaces}");
            Check(reduced.Faces.Count < source.Faces.Count * .6, "Redundant geometry is removed below the ceiling");
            Check(curvedFaces > flatFaces * 3, "Curved areas retain substantially more polygons than flat areas");
            Check(EulerCharacteristic(reduced) == 1, "Adaptive reduction introduces no holes");
            Check(reduced.Faces.All(f => Normal(reduced, f).Z > 0), "Triangles remain consistently oriented");
            Check(reduced.Vertices.Max(p => p[2]) > .7, "The high-relief detail survives");
            Check(source.Faces.Count == 1200, "Simplification does not mutate the source mesh");
        });
        tests.Run("Adaptive simplification preserves a plane and small valid meshes can receive textures", () =>
        {
            var source = meshes.Extract(PlaneVolume(), default);
            var reduced = simplifier.Reduce(source, 10000, default, .1);
            Check(reduced.Faces.Count < 20, "A flat patch needs only a few faces");
            Check(Math.Abs(Area(reduced) - Area(source)) < 1e-6, "The silhouette and area remain intact");
            Check(EulerCharacteristic(reduced) == 1, "The simplified patch remains a disk");

            var project = new ProjectData
            {
                Images = [new Photo { Id = "a", Width = 100, Height = 100, FocalPixels = 20 }]
            };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a", Centre = [15, 10, -30] }] };
            var map = new DepthMap { ImageId = "a", Width = 100, Height = 100, Depth = Enumerable.Repeat(30.25f, 10000).ToArray() };
            var textured = meshes.AssignCameras(project, scene, [map], reduced, default);
            Check(textured.Faces.Count == reduced.Faces.Count, "Valid low-polygon surfaces are not rejected by a face-count threshold");
        });
        tests.Run("Mesh repair and adaptive simplification respect cancellation", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ExpectCancellation(() => meshes.Extract(PlaneVolume(), cancellation.Token));
            ExpectCancellation(() => simplifier.Reduce(DetailSurface(), 10000, cancellation.Token, .1));
        });
        tests.Run("Texture view selection makes coherent regions and cannot select an occluded camera", () =>
        {
            var surface = new Surface();
            for (var y = 0; y <= 12; y++)
            {
                for (var x = 0; x <= 12; x++)
                {
                    surface.Vertices.Add([(x - 6) * .1, (y - 6) * .1, (x + y) % 2 == 0 ? .01 : -.01]);
                }
            }
            for (var y = 0; y < 12; y++)
            {
                for (var x = 0; x < 12; x++)
                {
                    var a = y * 13 + x;
                    surface.Faces.Add([a, a + 1, a + 14]);
                    surface.Faces.Add([a, a + 14, a + 13]);
                }
            }
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            foreach (var id in new[] { "left", "right", "occluded" })
            {
                project.Images.Add(new Photo { Id = id, Width = 100, Height = 100, FocalPixels = id == "occluded" ? 200 : 100 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [id == "left" ? -4 : 4, 0, -30] });
                maps.Add(new DepthMap { ImageId = id, Width = 100, Height = 100, Depth = Enumerable.Repeat(id == "occluded" ? 10f : 30f, 10000).ToArray() });
            }
            var assigned = meshes.AssignCameras(project, scene, maps, surface, default);
            var topology = SurfaceTopologyBuilder.Build(assigned, default);
            var seams = Enumerable.Range(0, topology.Neighbours.Count).Count(slot => topology.Neighbours[slot] >= 0
                && assigned.SourceImages[slot / 3] != assigned.SourceImages[topology.Neighbours[slot]]);
            Console.WriteLine($"  Coherent source selection: {seams / 2} seams across {surface.Faces.Count} faces");
            Check(assigned.Faces.Count == surface.Faces.Count, "All visible faces retain a photograph");
            Check(!assigned.SourceImages.Contains("occluded"), "Smoothness cannot override occlusion");
            Check(seams < surface.Faces.Count / 3, "Noisy normals do not create alternating triangle colours");
        });
        tests.Run("Repaired faces use observed boundary support without treating occlusion as missing depth", () =>
        {
            var project = new ProjectData
            {
                Images = [new Photo { Id = "a", Width = 100, Height = 100, FocalPixels = 50 }]
            };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "a" }] };
            var map = new DepthMap { ImageId = "a", Width = 100, Height = 100, Depth = Enumerable.Repeat(10f, 10000).ToArray() };
            var surface = new Surface
            {
                Vertices = [[-4, -4, 10], [4, -4, 10], [4, 4, 10], [-4, 4, 10]],
                Faces = [[0, 1, 2], [0, 2, 3]]
            };
            foreach (var depth in new[] { 0f, 5f })
            {
                for (var y = 42; y <= 44; y++)
                {
                    for (var x = 55; x <= 57; x++)
                    {
                        map.Depth[y * 100 + x] = depth;
                    }
                }
                var assigned = meshes.AssignCameras(project, scene, [map], surface, default);
                Check(assigned.Faces.Count == (depth == 0 ? 2 : 1),
                    "Unknown central depth can use agreeing boundary depths; an observed occluder cannot");
            }
        });
        tests.Run("Simplification does not collapse a closed component into duplicate triangles", () =>
        {
            var tetrahedron = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]],
                Faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]]
            };
            var reduced = simplifier.Reduce(tetrahedron, 1, default, 10);
            Check(reduced.Faces.Count == 4, "Topology takes precedence over an unreachable face budget");
            Check(EulerCharacteristic(reduced) == 2, "The closed component remains closed");
        });
    }

    private FusedVolume PlaneVolume()
    {
        var volume = new FusedVolume { VoxelSize = 1 };
        for (var x = 0; x <= 30; x++)
        {
            for (var y = 0; y <= 20; y++)
            {
                for (var z = -2; z <= 2; z++)
                {
                    volume.Samples.Add(new VolumeSample { X = x, Y = y, Z = z, Distance = z - .25f, Views = 3 });
                }
            }
        }
        return volume;
    }

    private Surface DetailSurface()
    {
        var surface = new Surface();
        for (var x = 0; x <= 30; x++)
        {
            for (var y = 0; y <= 20; y++)
            {
                var height = x <= 15 ? 0 : .8 * Math.Sin((x - 15) * .8) * Math.Sin(y * .6);
                surface.Vertices.Add([x, y, height]);
            }
        }
        for (var x = 0; x < 30; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                var a = x * 21 + y;
                surface.Faces.Add([a, a + 21, a + 22]);
                surface.Faces.Add([a, a + 22, a + 1]);
            }
        }
        return surface;
    }

    private V3 Normal(Surface mesh, int[] face)
    {
        var a = new V3(mesh.Vertices[face[0]]);
        return (new V3(mesh.Vertices[face[1]]) - a).Cross(new V3(mesh.Vertices[face[2]]) - a);
    }

    private double Area(Surface mesh) => mesh.Faces.Sum(face => Normal(mesh, face).Length * .5);

    private int EulerCharacteristic(Surface mesh)
    {
        var edges = new Dictionary<string, int>();
        var used = new HashSet<int>();
        foreach (var face in mesh.Faces)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var a = face[corner];
                var b = face[(corner + 1) % 3];
                var key = $"{Math.Min(a, b)}:{Math.Max(a, b)}";
                edges[key] = edges.GetValueOrDefault(key) + 1;
                used.Add(a);
            }
        }
        Check(edges.Values.All(count => count <= 2), "No non-manifold edges are introduced");
        return used.Count - edges.Count + mesh.Faces.Count;
    }

    private void ExpectCancellation(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected cancellation");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
