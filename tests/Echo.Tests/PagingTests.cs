using System.Text.Json;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Storage;
using Echo.Simplification;
using Echo.Pipeline;

namespace Echo.Tests;

public sealed class PagingTests
{
    private readonly IBulkArtifactStore artifacts;
    private readonly IDepthFusion fusion;
    private readonly IMeshBuilder meshes;
    private readonly IEdgeCollapse simplifier;

    public PagingTests(IBulkArtifactStore artifacts, IDepthFusion fusion, IMeshBuilder meshes, IEdgeCollapse simplifier)
    {
        this.artifacts = artifacts;
        this.fusion = fusion;
        this.meshes = meshes;
        this.simplifier = simplifier;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Mesh replacement releases old outputs while retaining no-op and failed inputs", () => InDirectory(directory =>
        {
            using var session = new BulkDataSession(65536 * 4, 0);
            using var work = new WorkBuffers(session, directory);
            using var buffers = new MeshGeometryBuffers(work);
            var original = buffers.Replace(scope =>
            {
                var result = scope!.Surface();
                result.Vertices.Add([1, 2, 3]);
                return result;
            });
            Check(ReferenceEquals(original, buffers.Replace(_ => original)) && original.Vertices[0][0] == 1, "No-op replacement disposed its input");
            var replacement = buffers.Replace(scope =>
            {
                var result = scope!.Surface();
                result.Vertices.Add(original.Vertices[0]);
                return result;
            });
            Check(replacement.Vertices[0][0] == 1, "Replacement lost data before copying completed");
            Check(Directory.EnumerateFiles(directory, "*.bin", SearchOption.AllDirectories).Count() == 3, "Old geometry files remained live");
            try
            {
                buffers.Replace(scope =>
                {
                    scope!.Surface().Vertices.Add([4, 5, 6]);
                    throw new OperationCanceledException();
                });
                throw new InvalidOperationException("Expected replacement cancellation");
            }
            catch (OperationCanceledException)
            {
            }
            Check(replacement.Vertices[0][0] == 1 && Directory.EnumerateFiles(directory, "*.bin", SearchOption.AllDirectories).Count() == 3,
                "Failed replacement discarded its input or leaked output files");
        }));
        tests.Run("Growing paged indexes retain insertion order and release superseded bucket files", () => InDirectory(directory =>
        {
            using var session = new BulkDataSession(65536 * 4, 0);
            using var work = new WorkBuffers(session, directory);
            var index = new BinaryHashIndex<int, int>(0, new IntRecordCodec(), new IntRecordCodec(), work);
            for (var value = 0; value < 5000; value++)
            {
                index.Add(value * -7919, value);
            }
            Check(index.Keys.SequenceEqual(Enumerable.Range(0, 5000).Select(value => value * -7919)), "Growing changed encounter order");
            for (var value = 0; value < 5000; value++)
            {
                Check(index[value * -7919] == value, "Growing lost a collision chain");
            }
            Check(Directory.EnumerateFiles(directory, "*.bin").Count() == 2, "Old bucket files remained live");
        }));
        tests.Run("Typed paged geometry appends preserve bits and avoid per-record arrays", () => InDirectory(directory =>
        {
            using var session = new BulkDataSession(65536 * 8, 0);
            using var work = new WorkBuffers(session, directory);
            var mesh = work.Surface();
            var writer = new SurfaceGeometryWriter(mesh);
            for (var index = 0; index < 1000; index++)
            {
                writer.AddVertex(new V3(-0d, double.Epsilon, index * Math.PI));
                writer.AddFace(new SurfaceTriangleVertices(index, index, index));
            }
            var reader = new SurfaceGeometryReader(mesh);
            Check(BitConverter.DoubleToInt64Bits(reader.Vertex(0).X) == long.MinValue, "Typed append lost signed zero");
            for (var repeat = 0; repeat < 100; repeat++)
            {
                reader.Triangle(reader.Face(repeat));
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sum = 0d;
            for (var repeat = 0; repeat < 10000; repeat++)
            {
                sum += reader.Triangle(reader.Face(repeat % 1000)).A.Z;
            }
            Check(sum > 0 && GC.GetAllocatedBytesForCurrentThread() - before < 4096, "Paged geometry allocated small arrays per query");
            var count = mesh.Faces.Count;
            try
            {
                ((PagedList<int[]>)mesh.Faces).AddEncodedRecords(new byte[13]);
                throw new InvalidOperationException("Partial record was accepted");
            }
            catch (ArgumentException)
            {
            }
            Check(mesh.Faces.Count == count, "Rejected append changed the record count");
        }));
        tests.Run("Parallel reduction preserves geometry across resident and paged budgets", () => InDirectory(directory =>
        {
            var source = new Surface();
            const int side = 24;
            for (var y = 0; y <= side; y++)
            {
                for (var x = 0; x <= side; x++)
                {
                    source.Vertices.Add([x, y, Math.Sin(x * .3) * Math.Cos(y * .2)]);
                }
            }
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var first = y * (side + 1) + x;
                    source.Faces.Add([first, first + 1, first + side + 2]);
                    source.Faces.Add([first, first + side + 2, first + side + 1]);
                }
            }
            var expected = ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, 200, default, .01, null, null, null, 4, 1);
            foreach (var budget in new[] { 65536L, 16 * 1048576L })
            {
                using var session = artifacts.OpenSession(budget, 0);
                using (var work = new WorkBuffers(session, Path.Combine(directory, budget.ToString())))
                {
                    var actual = ParallelMeshCollapse.Reduce((EdgeCollapse)simplifier, source, 200, default, .01, null, work, null, 4, 4);
                    Check(actual.Faces.Count == expected.Faces.Count && actual.Vertices.Count == expected.Vertices.Count,
                        "Shared memory pressure changed parallel output counts");
                    Check(actual.Vertices.Zip(expected.Vertices).All(pair => pair.First.SequenceEqual(pair.Second))
                        && actual.Faces.Zip(expected.Faces).All(pair => pair.First.SequenceEqual(pair.Second)),
                        "Paging changed parallel geometry or seam joining");
                }
                Check(session.Statistics.ResidentBytes == 0 && session.Statistics.ScratchBytes == 0,
                    "Parallel reduction leaked scratch records after disposal");
            }
        }));
        tests.Run("Resident scratch spills within the shared budget and releases its reservations", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(1048576, 0);
            using var work = new WorkBuffers(session, directory);
            using (var resident = work.CreateScope(preferResident: true))
            {
                var values = resident.List(new IntRecordCodec());
                for (var index = 0; index < 400000; index++)
                {
                    values.Add(index * 3);
                }
                values[12345] = -17;
                values.RemoveAt(values.Count - 1);
                values.Add(9000000);
                Check(values.Count == 400000 && values[12345] == -17 && values[^1] == 9000000, "Spilling changed mutable records");
                for (var index = 0; index < values.Count - 1; index += 997)
                {
                    Check(values[index] == index * 3, "Spilling lost a block or changed record order");
                }
                Check(session.Statistics.PeakResidentBytes <= 1048576 && session.Statistics.Evictions > 0, "Typed records escaped the memory budget or did not exercise fallback");
            }
            Check(session.Statistics.ResidentBytes == 0 && session.Statistics.ScratchBytes == 0, "Typed scratch retained memory or disk after disposal");
        }));
        tests.Run("Parallel scratch scopes share one memory budget without sharing mutable lists", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(4 * 1048576, 0);
            using var work = new WorkBuffers(session, directory);
            var scopes = Enumerable.Range(0, 4).Select(_ => work.CreateScope(preferResident: true)).ToArray();
            Parallel.For(0, scopes.Length, worker =>
            {
                var values = scopes[worker].List(new IntRecordCodec());
                for (var index = 0; index < 300000; index++)
                {
                    values.Add(index + worker);
                }
                Check(values[12345] == 12345 + worker && values[^1] == 299999 + worker, "Concurrent scratch lists corrupted each other");
                scopes[worker].Dispose();
            });
            Check(session.Statistics.PeakResidentBytes <= 4 * 1048576 && session.Statistics.ResidentBytes == 0, "Parallel reservations exceeded or leaked the shared budget");
        }));
        tests.Run("Adjacency removes dead slots while preserving recycled encounter order", () =>
        {
            var adjacency = new VertexFacesStore(2, null);
            var expected = new HashSet<int>();
            var random = new Random(179);
            for (var index = 0; index < 5000; index++)
            {
                var face = random.Next(40);
                if (random.Next(2) == 0)
                {
                    expected.Add(face);
                    adjacency.Add(0, face);
                }
                else
                {
                    Check(adjacency.Remove(0, face) == expected.Remove(face), "Adjacency removal disagrees with membership");
                }
                Check(adjacency.Faces(0).SequenceEqual(expected) && adjacency.Count(0) == expected.Count,
                    "Unlinking or recycling a slot changed deterministic face order");
            }
        });
        tests.Run("Candidate refresh and linear pruning preserve costs and reusable handles", () =>
        {
            var queue = new CollapseCandidateQueue(32, null, default);
            for (var index = 0; index < 1000; index++)
            {
                queue.Offer(new Candidate(index, index + 1, 0, 0, new V3(index, 1, 2), index), index);
            }
            Check(queue.Refresh(41, 40, 7, 9), "Existing cost could not be refreshed");
            queue.Retain(candidate => candidate.Error % 2 == 0 && candidate.Error < 100, default);
            Check(queue.Count == 50, "Pruning retained ineligible candidates");
            queue.Offer(new Candidate(1001, 1002, 0, 0, default, 3), 3);
            Check(queue.AllocatedHandles == 1000, "Pruning did not recycle removed handles");
            var previous = -1d;
            while (queue.TryDequeue(out var candidate))
            {
                Check(candidate.Error >= previous, "Pruning did not rebuild a valid heap");
                previous = candidate.Error;
                if (candidate.A == 40)
                {
                    Check(candidate.RevisionA == 9 && candidate.RevisionB == 7 && candidate.Position == new V3(40, 1, 2), "Refresh changed geometry or reversed revisions");
                }
            }
        });
        tests.Run("Repeated record fills cross pages, append correctly and retain codec tables", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var values = (PagedList<V3>)work.List(new V3RecordCodec());
            values.Add(new V3(1, 2, 3));
            var point = new V3(Math.PI, -0d, double.Epsilon);
            values.AddRepeated(point, 10003);
            values.AddRepeated(new V3(4, 5, 6), 2);
            values.AddRepeated(default, 0);
            Check(values.Count == 10006 && values[0] == new V3(1, 2, 3) && values[^1] == new V3(4, 5, 6), "Fill overwrote neighbouring records");
            for (var index = 1; index <= 10003; index++)
            {
                var value = values[index];
                Check(value.X == point.X && BitConverter.DoubleToInt64Bits(value.Y) == long.MinValue && value.Z == point.Z,
                    "Fill changed record bytes across a page boundary");
            }
            var strings = (PagedList<string>)work.Filled(20003, "same", new StringTableCodec());
            strings.Add("next");
            Check(strings[0] == "same" && strings[20002] == "same" && strings[20003] == "next", "Fill did not preserve the codec's string table");
            try
            {
                values.AddRepeated(default, 10, new CancellationToken(true));
                throw new InvalidOperationException("Fill ignored cancellation");
            }
            catch (OperationCanceledException)
            {
                Check(values.Count == 10006, "Cancelled fill appended records");
            }
            Check(session.Statistics.Evictions > 0, "Fill test did not exercise paging");
        }));
        tests.Run("Mesh filters handle multiple paged batches and release private scratch", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var surface = work.Surface();
            var fixture = new FusionFixture();
            fixture.Views.Add(new DepthView(new Photo { Id = "third", Width = 64, Height = 64, FocalPixels = 64 },
                new CameraPose { ImageId = "third", Centre = [.5, 0, 0] },
                new DepthMap { ImageId = "third", Width = 64, Height = 64, Depth = Enumerable.Repeat(10f, 4096).ToArray() }));
            // 5003 faces and 15009 vertices exercise full batches and short tails.
            for (var index = 0; index < 5003; index++)
            {
                var start = surface.Vertices.Count;
                var z = index % 5 == 0 ? 8d : 10d;
                surface.Vertices.Add([-.1, -.1, z]);
                surface.Vertices.Add([.1, -.1, z]);
                surface.Vertices.Add([0, .1, z]);
                surface.Faces.Add([start, start + 1, start + 2]);
                surface.SourceImages.Add("photo-" + index % 3);
            }
            var expected = surface.Faces.Count - (surface.Faces.Count + 4) / 5;
            var visible = SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, default, work);
            Check(visible.Faces.Count == expected && visible.Vertices.All(point => point[2] == 10), "Visibility batch tail reused stale flags");
            Check(visible.SourceImages.SequenceEqual(Enumerable.Range(0, 5003).Where(index => index % 5 != 0).Select(index => "photo-" + index % 3)), "Photographs lost encounter order");
            var observed = new Surface { Vertices = [[-10, -10, 10], [10, -10, 10], [0, 10, 10]], Faces = [[0, 1, 2]] };
            var supported = SurfaceSupportFilter.Filter(surface, observed, .2, default, work);
            Check(supported.Faces.Count == expected && supported.Vertices.All(point => point[2] == 10), "Support batching changed retained geometry");
            var fragments = SurfaceFragments.RemoveSmall(observed, .1, default, work);
            var trimmed = SurfaceIslandTrimmer.Trim(observed, IslandTrimMode.Main, 0, default, work);
            Check(fragments.Faces.Count == 1 && ReferenceEquals(trimmed.Surface, observed), "Scope disposal released output geometry");
            Check(!Directory.EnumerateFiles(directory, "*.bin", SearchOption.AllDirectories).Any(path => Path.GetDirectoryName(path) != directory), "Mesh filters retained private scratch files");
            using var cancelled = new CancellationTokenSource();
            try
            {
                SurfaceVisibilityFilter.Filter(surface, fixture.Views, .01, cancelled.Token, work, _ => cancelled.Cancel());
                throw new InvalidOperationException("Visibility filtering ignored cancellation between batches");
            }
            catch (OperationCanceledException)
            {
            }
            Check(!Directory.EnumerateFiles(directory, "*.bin", SearchOption.AllDirectories).Any(path => Path.GetDirectoryName(path) != directory), "Cancellation retained private scratch files");
            Check(surface.Faces.Count == 5003 && surface.Vertices[0][2] == 8, "Filtering mutated its source");
        }));
        tests.Run("Indexed collapse queue updates and removes edges without accumulating obsolete candidates", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var queue = new CollapseCandidateQueue(16, work, default);
            var expected = new Dictionary<Edge, IndexedCandidate>();
            var random = new Random(7641);
            var peak = 0;
            for (var step = 0; step < 20000; step++)
            {
                var a = random.Next(80);
                var b = a + 1 + random.Next(80);
                var key = new Edge(a, b);
                if (step % 7 == 0 && expected.Count > 0)
                {
                    var minimum = expected.Values.MinBy(entry => entry.Priority);
                    Check(queue.TryDequeue(out var candidate) && candidate == minimum.Value, "Updating a candidate broke minimum ordering");
                    expected.Remove(new Edge(candidate.A, candidate.B));
                }
                else if (step % 3 == 0)
                {
                    Check(queue.Remove(b, a) == expected.Remove(key), "Removing an edge lost its index chain");
                }
                else
                {
                    var candidate = new Candidate(a, b, step, step, new V3(a, b, step), step);
                    var priority = random.NextDouble() * 10000 + step * 1e-9;
                    queue.Offer(candidate, priority);
                    expected[key] = new IndexedCandidate(candidate, priority, 0);
                    peak = Math.Max(peak, expected.Count);
                }
                Check(queue.Count == expected.Count && queue.AllocatedHandles <= peak,
                    "Repeated offers grew the queue or failed to reuse removed handles");
            }
            foreach (var entry in expected.Values.OrderBy(entry => entry.Priority))
            {
                Check(queue.TryDequeue(out var candidate) && candidate == entry.Value, "The final queue order or revision differs");
            }
            Check(!queue.TryDequeue(out _) && session.Statistics.Evictions > 0, "The paged queue did not exercise eviction");
        }));
        tests.Run("Unchanged collapse costs retain tie order while updating candidate revisions", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var original = new CollapseCandidateQueue(16, null, default);
            var updated = new CollapseCandidateQueue(16, work, default);
            for (var index = 0; index < 2000; index++)
            {
                var candidate = new Candidate(index, index + 2000, 0, 0, new V3(index, 0, 0), index % 5);
                original.Offer(candidate, index % 5);
                updated.Offer(candidate, index % 5);
            }
            for (var index = 1999; index >= 0; index--)
            {
                updated.Offer(new Candidate(index, index + 2000, 7, 9, new V3(index, 1, 2), index % 5), index % 5);
            }
            while (original.TryDequeue(out var expected))
            {
                var sameEdge = updated.TryDequeue(out var actual) && actual.A == expected.A && actual.B == expected.B;
                Check(sameEdge, "Updating unchanged priorities reordered tied candidates");
                var refreshed = actual.RevisionA == 7 && actual.RevisionB == 9 && actual.Position == new V3(expected.A, 1, 2);
                Check(refreshed, "Skipping heap repair lost the new revision or position");
            }
            Check(updated.Count == 0 && session.Statistics.Evictions > 0, "The updated paged queue lost candidates or did not exercise eviction");
        }));
        tests.Run("Temporary scopes discard dirty pages and return scratch space without flushing", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(4 * 65536, 2 * 65536);
            using var work = new WorkBuffers(session, directory);
            var retained = work.List(new IntRecordCodec());
            retained.Add(42);
            var writes = session.Statistics.WrittenBytes;
            IList<int> released;
            using (var scope = work.CreateScope())
            {
                released = scope.List(new IntRecordCodec());
                released.Add(123);
                Check(session.Statistics.ScratchBytes == 2 * 65536, "Scratch usage was not charged");
            }
            Check(session.Statistics.WrittenBytes == writes, "Disposing temporary buffers wrote dirty pages");
            Check(session.Statistics.ScratchBytes == 65536 && retained[0] == 42, "Scope disposal released another scope's data or failed to return space");
            try
            {
                _ = released[0];
                throw new InvalidOperationException("A discarded buffer remained readable");
            }
            catch (ObjectDisposedException)
            {
            }
            using (var next = work.CreateScope())
            {
                next.List(new IntRecordCodec()).Add(456);
            }
            Check(Directory.EnumerateFiles(directory, "*.bin", SearchOption.AllDirectories).Count() == 1, "Discarded files remained on disk");
            using var output = new MemoryStream();
            ((PagedList<int>)retained).CopyEncodedRecordsTo(output);
            Check(BitConverter.ToInt32(output.ToArray()) == 42, "Unflushed scratch output could not be published");
        }));
        tests.Run("Simplification vertex records preserve coefficients without decoding allocations", () =>
        {
            var vertex = new Vertex { Id = 23, Position = new V3(-1.5, Math.PI, 12), PlaneCount = 500, Revision = 17, Removed = true };
            for (var index = 0; index < 16; index++)
            {
                vertex[index] = (index - 5) * Math.PI;
            }
            var codec = new VertexRecordCodec();
            Span<byte> bytes = stackalloc byte[165];
            codec.Encode(vertex, bytes);
            var decoded = codec.Decode(bytes);
            Check(decoded.Id == vertex.Id && decoded.Position == vertex.Position, "Vertex identity or position changed");
            Check(decoded.Removed && decoded.Revision == 17 && decoded.PlaneCount == 500, "Vertex state changed");
            for (var index = 0; index < 16; index++)
            {
                Check(decoded[index] == vertex[index], "A quadric coefficient changed");
            }
            for (var index = 0; index < 1000; index++)
            {
                decoded = codec.Decode(bytes);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sum = 0.0;
            for (var index = 0; index < 10000; index++)
            {
                sum += codec.Decode(bytes)[7];
            }
            Check(GC.GetAllocatedBytesForCurrentThread() - before < 1024 && sum > 0,
                "Numeric vertex reads allocated managed objects");
        });
        tests.Run("Binary surface edge indexes survive collisions and eviction without a database", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(512, new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec(), work, default);
            var expected = new Dictionary<SurfaceEdge, BoundaryEdge>();
            for (var index = 0; index < 4000; index++)
            {
                var key = new SurfaceEdge(index % 17, index + 100);
                var value = new BoundaryEdge(key.A, key.B, new V3(.1, .2, .3), 1);
                edges.Add(key, value);
                expected.Add(key, value);
            }
            foreach (var key in expected.Keys.ToArray())
            {
                var value = expected[key] with
                {
                    Count = 2
                };
                edges[key] = value;
                expected[key] = value;
            }
            foreach (var entry in expected)
            {
                Check(edges.TryGetValue(entry.Key, out var value) && value == entry.Value, "Hash collisions lost or changed an edge");
            }
            Check(!edges.ContainsKey(new SurfaceEdge(100, 100)) && edges.Count == expected.Count, "Missing edge or count is incorrect");
            Check(session.Statistics.Evictions > 0 && session.Statistics.PeakResidentBytes <= 65536, "Edge index exceeded its page budget");
            Check(!Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.AllDirectories).Any(), "Surface edges still created a database");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                _ = new BinaryHashIndex<SurfaceEdge, int>(100, new SurfaceEdgeRecordCodec(), new IntRecordCodec(), work, cancelled.Token);
                throw new InvalidOperationException("Cancelled edge indexing continued");
            }
            catch (OperationCanceledException)
            {
            }
        }));
        tests.Run("Mesh compaction uses direct vertex remaps and retains photo assignments", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var source = new Surface
            {
                Vertices = [[100, 100, 100], [1, 0, 0], [0, 1, 0], [0, 0, 0]],
                Faces = [[3, 1, 2], [2, 1, 3]],
                SourceImages = ["first", "second"]
            };
            var visible = SurfaceVisibilityFilter.Filter(source, Array.Empty<DepthView>(), .01, default, work);
            Check(visible.Vertices.Count == 3 && visible.Vertices[0].SequenceEqual(source.Vertices[3]), "Unused vertex compaction changed encounter order");
            Check(visible.Faces[0].SequenceEqual([0, 1, 2]) && visible.Faces[1].SequenceEqual([2, 1, 0]), "Compaction changed face winding or shared vertices");
            Check(visible.SourceImages.SequenceEqual(source.SourceImages), "Visibility compaction lost photograph assignments");
            var supported = SurfaceSupportFilter.Filter(source, source, .1, default, work);
            var fragments = SurfaceFragments.RemoveSmall(source, .1, default, work);
            Check(supported.Faces.Count == 2 && fragments.Faces.Count == 2, "Compaction removed supported faces");
            Check(!Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.AllDirectories).Any(), "Dense vertex IDs were remapped through a database");
        }));
        tests.Run("Resident geometry reservations share the page budget and preserve dirty data", () => InDirectory(directory =>
        {
            const int capacity = 8 * 65536;
            using var session = artifacts.OpenSession(capacity, 0);
            var records = session.Open(Path.Combine(directory, "values.bin"), new IntRecordCodec(), true);
            for (var index = 0; index < 100000; index++)
            {
                records.Add(index);
            }
            var reservation = records.TryReserveMemory(3 * 65536 + 1);
            Check(reservation != null, "Working memory was not reserved from the page budget");
            Check(records.TryReserveMemory(capacity) == null, "Oversized reservation should fall back without changing the budget");
            for (var index = 0; index < records.Count; index++)
            {
                Check(records[index] == index, "Reserving memory lost dirty records");
            }
            Check(session.Statistics.Evictions > 0 && session.Statistics.PeakResidentBytes <= capacity,
                "Pages plus reservations exceeded the shared memory budget");
            reservation!.Dispose();
            reservation.Dispose();
            using var reclaimed = records.TryReserveMemory(7 * 65536);
            Check(reclaimed != null, "Disposing a reservation did not return its budget exactly once");
        }));
        tests.Run("Paged triangle queries match memory without per-query page reads", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(8 * 65536, 0);
            var work = new WorkBuffers(session, directory);
            var paged = work.Surface();
            var memory = new Surface();
            for (var index = 0; index < 250; index++)
            {
                var start = memory.Vertices.Count;
                var x = index % 25;
                var y = index / 25;
                memory.Vertices.Add([x, y, 0]);
                memory.Vertices.Add([x + .8, y, .1]);
                memory.Vertices.Add([x, y + .8, -.1]);
                memory.Faces.Add([start, start + 1, start + 2]);
            }
            foreach (var vertex in memory.Vertices)
            {
                paged.Vertices.Add(vertex);
            }
            foreach (var face in memory.Faces)
            {
                paged.Faces.Add(face);
            }
            using var expected = new SurfaceTriangleIndex(memory, default);
            var beforeConstruction = session.Statistics.ResidentBytes;
            using (var actual = new SurfaceTriangleIndex(paged, default))
            {
                var beforeQueries = session.Statistics;
                Parallel.For(0, 2000, query =>
                {
                    var point = new V3(query % 25 + .2, query % 10 + .2, 1);
                    var end = point - new V3(0, 0, 2);
                    Check(actual.HasOccluder(point, end) == expected.HasOccluder(point, end), "Paged ray visibility changed");
                    Check(actual.Nearest(point, 100) == expected.Nearest(point, 100), "Paged closest point changed");
                    var patch = new SurfaceTriangle(point, end, point + new V3(.3, .3, 0));
                    Check(actual.Intersects(patch) == expected.Intersects(patch), "Paged intersection changed");
                });
                var afterQueries = session.Statistics;
                Check(beforeQueries.Hits == afterQueries.Hits && beforeQueries.Misses == afterQueries.Misses,
                    "Parallel geometry queries still acquire the page cache");
                var addition = new SurfaceTriangle(new V3(100, 100, 0), new V3(101, 100, 0), new V3(100, 101, 0));
                actual.Add(addition);
                Check(actual.Intersects(new SurfaceTriangle(new V3(100.2, 100.2, -1), new V3(100.2, 100.2, 1), new V3(100.4, 100.2, 0))),
                    "An appended hole patch was not included in intersection queries");
                Check(afterQueries.PeakResidentBytes <= 8 * 65536, "Geometry snapshot exceeded its page budget");
            }
            Check(session.Statistics.ResidentBytes == beforeConstruction, "Geometry snapshot retained its reservation after disposal");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                using var interrupted = new SurfaceTriangleIndex(paged, cancelled.Token);
                throw new InvalidOperationException("Cancelled index creation continued");
            }
            catch (OperationCanceledException)
            {
                Check(session.Statistics.ResidentBytes == beforeConstruction, "Cancelled snapshot retained its reservation");
            }
        }));
        tests.Run("Triangle queries retain a paged fallback when a snapshot will not fit", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var surface = work.Surface();
            surface.Vertices.Add([0, 0, 0]);
            surface.Vertices.Add([1, 0, 0]);
            surface.Vertices.Add([0, 1, 0]);
            surface.Faces.Add([0, 1, 2]);
            using var index = new SurfaceTriangleIndex(surface, default);
            Check(index.HasOccluder(new V3(.2, .2, 1), new V3(.2, .2, -1)), "Low-memory visibility changed");
            Check(session.Statistics.PeakResidentBytes <= 65536, "Snapshot ignored a small memory budget");
        }));
        tests.Run("Initial edge indexing preserves first encounter order without a database", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var paged = new InitialEdgeIndex(2048, work, default);
            var memory = new InitialEdgeIndex(2048, null, default);
            var expected = new HashSet<Edge>();
            var random = new Random(52);
            for (var index = 0; index < 20000; index++)
            {
                var a = index < 4000 ? 0 : random.Next(100);
                var b = index < 4000 ? index : random.Next(100);
                paged.Add(a, b);
                paged.Add(b, a);
                memory.Add(a, b);
                expected.Add(new Edge(Math.Min(a, b), Math.Max(a, b)));
            }
            Check(paged.Count == expected.Count && paged.Edges().SequenceEqual(expected), "Paged edge membership or order changed");
            Check(memory.Edges().SequenceEqual(expected), "In-memory edge membership or order changed");
            Check(!Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.AllDirectories).Any(), "Initial edge indexing still created a database");
        }));
        tests.Run("Paged vertex adjacency preserves membership and slot order without a global index", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var store = new VertexFacesStore(16, work);
            var expected = Enumerable.Range(0, 16).Select(_ => new HashSet<int>()).ToArray();
            var random = new Random(721);
            for (var iteration = 0; iteration < 5000; iteration++)
            {
                var vertex = random.Next(16);
                var face = random.Next(100);
                if (random.Next(3) == 0)
                {
                    Check(store.Remove(vertex, face) == expected[vertex].Remove(face), "Removal result changed");
                }
                else
                {
                    store.Add(vertex, face);
                    expected[vertex].Add(face);
                }
                Check(store.Contains(vertex, face) == expected[vertex].Contains(face), "Membership changed");
                Check(store.Count(vertex) == expected[vertex].Count, "Adjacency count changed");
                Check(store.Faces(vertex).SequenceEqual(expected[vertex]), "Slot reuse changed candidate ordering");
            }
            for (var face = 0; face < 2000; face++)
            {
                store.Add(0, face);
                expected[0].Add(face);
            }
            Check(store.Faces(0).SequenceEqual(expected[0]) && store.Contains(0, 1999), "High-valence vertices lost faces");
            Check(!Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.AllDirectories).Any(), "Vertex adjacency still created a global database index");
        }));
        tests.Run("Paged records preserve bytes across dirty eviction and reopen", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "records.bin");
            using (var session = artifacts.OpenSession(65536, 0))
            {
                var records = session.Open(path, new VectorRecordCodec(), true);
                for (var index = 0; index < 20000; index++)
                {
                    records.Add([index, -index - .125, Math.PI]);
                }
                for (var index = records.Count - 1; index >= 0; index--)
                {
                    Check(records[index].SequenceEqual(new[] { (double)index, -index - .125, Math.PI }), "Dirty page lost a record");
                }
                Check(session.Statistics.Evictions > 0, "Test did not force eviction");
                Check(session.Statistics.PeakResidentBytes == 65536, "Page cache exceeded its budget");
            }
            using var reopened = artifacts.OpenSession(65536, 0);
            Check(reopened.Open(path, new VectorRecordCodec())[19999][2] == Math.PI, "Reopened binary changed precision");
        }));
        tests.Run("Binary indexes preserve insertion order, duplicate semantics and signed keys", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var records = new BinaryHashIndex<Echo.Fusion.Grid, int>(16, new Echo.Fusion.GridRecordCodec(), new IntRecordCodec(), work);
            var coordinates = new[] { 8, -2, int.MinValue, 0, int.MaxValue };
            foreach (var x in coordinates)
            {
                records.Add(new Echo.Fusion.Grid(x, 0, 0), x);
            }
            records[new Echo.Fusion.Grid(-2, 0, 0)] = 42;
            Check(records.Select(pair => pair.Key.X).SequenceEqual(coordinates), "Dictionary insertion order changed");
            Check(records[new Echo.Fusion.Grid(-2, 0, 0)] == 42, "Dictionary update failed");
            Check(records.Keys.Select(key => key.X).SequenceEqual(coordinates), "Key iteration changed insertion order");
            Check(!records.TryAdd(new Echo.Fusion.Grid(-2, 0, 0), 7) && records.Count == coordinates.Length, "A duplicate was appended");
            Check(records.Values.SequenceEqual([8, 42, int.MinValue, 0, int.MaxValue]), "Value iteration differs from entry order");
        }));
        tests.Run("Binary position indexes retain signed zero and NaN equality", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var positions = new BinaryHashIndex<V3, int>(16, new PositionKeyCodec(), new IntRecordCodec(), work);
            positions.Add(new V3(0, -0d, 1), 12);
            Check(!positions.TryAdd(new V3(-0d, 0, 1), 13), "Signed zero changed vertex welding");
            positions.Add(new V3(double.NaN, 2, 3), 14);
            var anotherNaN = BitConverter.Int64BitsToDouble(0x7ff8000000000001);
            Check(!positions.TryAdd(new V3(anotherNaN, 2, 3), 15), "NaN encodings changed key equality");
            Check(positions[new V3(-0d, 0, 1)] == 12 && positions[new V3(anotherNaN, 2, 3)] == 14,
                "A canonical position lost its original value");
        }));
        tests.Run("Binary indexes enforce the shared scratch disk budget", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 2 * 65536);
            var work = new WorkBuffers(session, directory);
            var records = new BinaryHashIndex<int, int>(4096, new IntRecordCodec(), new IntRecordCodec(), work);
            try
            {
                for (var index = 0; index < 100000; index++)
                {
                    records.Add(index, index);
                }
                throw new InvalidOperationException("Binary indexing ignored the scratch limit");
            }
            catch (EchoException)
            {
                Check(session.Statistics.ScratchBytes <= 2 * 65536, "Scratch budget was exceeded before rejection");
            }
        }));
        tests.Run("Candidate heap compaction removes obsolete entries and remains ordered", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var heap = new PagedMinHeap<int>(session.Open(Path.Combine(directory, "compact.bin"), new HeapEntryCodec<int>(new IntRecordCodec()), true));
            for (var index = 0; index < 20000; index++)
            {
                heap.Enqueue(index, index % 127);
            }
            var removed = heap.Retain(value => value % 3 == 0, default);
            Check(removed == 13333 && heap.Count == 6667, "Compaction lost or retained the wrong candidates");
            heap.Enqueue(20001, -1);
            var priority = double.NegativeInfinity;
            var seen = new HashSet<int>();
            while (heap.TryDequeue(out var value, out var next))
            {
                Check(next >= priority && value % 3 == 0 && seen.Add(value), "Compaction broke heap ordering or duplicated a candidate");
                priority = next;
            }
            Check(seen.Count == 6668, "Compacted queue lost valid candidates");
            Check(heap.Retain(_ => false, default) == 0, "Empty compaction failed");
            heap.Enqueue(1, 1);
            Check(heap.Retain(_ => false, default) == 1 && heap.Count == 0, "Complete removal failed");
            heap.Enqueue(2, 2);
            Check(heap.TryDequeue(out var final, out _) && final == 2, "Cleared heap could not be reused");
        }));
        tests.Run("Paged heap preserves framework priority ordering including ties", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var heap = new PagedMinHeap<int>(session.Open(Path.Combine(directory, "heap.bin"), new HeapEntryCodec<int>(new IntRecordCodec()), true));
            var baseline = new PriorityQueue<int, double>();
            var random = new Random(721);
            for (var index = 0; index < 30000; index++)
            {
                if (baseline.Count > 0 && random.Next(3) == 0)
                {
                    baseline.TryDequeue(out var expected, out var cost);
                    Check(heap.TryDequeue(out var actual, out var priority) && actual == expected && priority == cost, "Heap ordering changed");
                }
                else
                {
                    var priority = random.Next(100);
                    baseline.Enqueue(index, priority);
                    heap.Enqueue(index, priority);
                }
            }
            while (baseline.TryDequeue(out var expected, out var cost))
            {
                Check(heap.TryDequeue(out var actual, out var priority) && actual == expected && priority == cost, "Heap drain ordering changed");
            }
            Check(!heap.TryDequeue(out _, out _), "Heap retained extra entries");
        }));
        tests.Run("Paged island connectivity preserves exact welding and component selection", () => InDirectory(directory =>
        {
            var source = new Surface
            {
                Vertices = [[0, 0, 0], [2, 0, 0], [0, 2, 0], [-0d, 2, -0d], [2, -0d, 0], [2, 2, 0], [10, 0, 0], [11, 0, 0], [10, 1, 0]],
                Faces = [[0, 1, 2], [3, 4, 5], [6, 7, 8]],
                SourceImages = ["a", "b", "c"]
            };
            var baseline = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, .1, default);
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var result = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, .1, default, work);
            Check(result.RemovedFaces == 1 && result.RemovedFaces == baseline.RemovedFaces, "Paged welding changed component membership");
            Check(result.Surface.SourceImages.SequenceEqual(baseline.Surface.SourceImages), "Island trimming changed photographic assignments");
            for (var index = 0; index < baseline.Surface.Faces.Count; index++)
            {
                Check(result.Surface.Faces[index].SequenceEqual(baseline.Surface.Faces[index]), "Island trimming changed face order");
            }
        }));
        tests.Run("Paged fusion and extraction match in-memory output exactly", () => InDirectory(directory =>
        {
            var fixture = new FusionFixture();
            var resources = new FusionResourcePlan(25600000, 100000);
            var baseline = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
            var baselineMesh = meshes.Extract(baseline, default);
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var result = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default, work: work);
            Check(result.VoxelSize == baseline.VoxelSize && result.Samples.Count == baseline.Samples.Count, "Fusion count or spacing changed");
            for (var index = 0; index < baseline.Samples.Count; index++)
            {
                Check(Same(result.Samples[index], baseline.Samples[index]), "Fusion changed an ordered sample");
            }
            var mesh = meshes.Extract(result, default, work);
            Check(mesh.Vertices.Count == baselineMesh.Vertices.Count && mesh.Faces.Count == baselineMesh.Faces.Count, "Extraction counts changed");
            for (var index = 0; index < mesh.Vertices.Count; index++)
            {
                Check(mesh.Vertices[index].SequenceEqual(baselineMesh.Vertices[index]), "Extraction changed a vertex");
            }
            for (var index = 0; index < mesh.Faces.Count; index++)
            {
                Check(mesh.Faces[index].SequenceEqual(baselineMesh.Faces[index]), "Extraction changed face ordering or welding");
            }
            var supported = SurfaceSupportFilter.Filter(baselineMesh, baselineMesh, .1, default);
            var pagedSupport = SurfaceSupportFilter.Filter(baselineMesh, baselineMesh, .1, default, work);
            Check(supported.Vertices.Zip(pagedSupport.Vertices).All(pair => pair.First.SequenceEqual(pair.Second))
                && supported.Faces.Zip(pagedSupport.Faces).All(pair => pair.First.SequenceEqual(pair.Second))
                && supported.Faces.Count == pagedSupport.Faces.Count, "Paged completion support changed topology");
            var vegetation = baselineMesh.Vertices.Select(point => point[0] > 0).ToArray();
            var tolerances = SurfaceVegetationClassifier.Tolerances(baselineMesh, vegetation, .1);
            var pagedTolerances = SurfaceVegetationClassifier.Tolerances(baselineMesh, vegetation, .1, work);
            Check(tolerances.SequenceEqual(pagedTolerances), "Paged vegetation changed protected material boundaries");
            foreach (var point in baselineMesh.Vertices)
            {
                point[2] += Math.Sin(point[0] * 3) * Math.Cos(point[1] * 2) * .15;
            }
            var reduced = simplifier.Reduce(baselineMesh, 80, default, .01);
            var pagedReduction = simplifier.Reduce(baselineMesh, 80, default, .01, work: work);
            Check(reduced.Vertices.Count == pagedReduction.Vertices.Count && reduced.Faces.Count == pagedReduction.Faces.Count, "Paged simplification changed counts");
            for (var index = 0; index < reduced.Vertices.Count; index++)
            {
                Check(reduced.Vertices[index].SequenceEqual(pagedReduction.Vertices[index]), "Paged simplification changed positions");
            }
            for (var index = 0; index < reduced.Faces.Count; index++)
            {
                Check(reduced.Faces[index].SequenceEqual(pagedReduction.Faces[index]), "Paged simplification changed topology");
            }
        }));
    }

    private bool Same(VolumeSample left, VolumeSample right)
        => left.X == right.X && left.Y == right.Y && left.Z == right.Z
            && BitConverter.SingleToInt32Bits(left.Distance) == BitConverter.SingleToInt32Bits(right.Distance) && left.Views == right.Views;

    private void InDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-paging-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
