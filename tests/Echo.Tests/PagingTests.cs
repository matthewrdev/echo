using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Storage;
using Echo.Simplification;

namespace Echo.Tests;

[Export]
public sealed class PagingTests
{
    private readonly IBulkArtifactStore artifacts;
    private readonly IDepthFusion fusion;
    private readonly IMeshBuilder meshes;
    private readonly IEdgeCollapse simplifier;

    [ImportingConstructor]
    public PagingTests(IBulkArtifactStore artifacts, IDepthFusion fusion, IMeshBuilder meshes, IEdgeCollapse simplifier)
    {
        this.artifacts = artifacts;
        this.fusion = fusion;
        this.meshes = meshes;
        this.simplifier = simplifier;
    }

    internal void Run(TestSuite tests)
    {
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
            using var work = new WorkBuffers(session, directory, 1024, 0);
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
            using var work = new WorkBuffers(session, directory, 1024, 0);
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
            using var work = new WorkBuffers(session, directory, 1024, 0);
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
            Check(work.IndexDiskBytes == 0, "Initial edge indexing still created a database");
        }));
        tests.Run("Paged vertex adjacency preserves membership and slot order without a global index", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory, 1024, 0);
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
            Check(work.IndexDiskBytes == 0, "Vertex adjacency still created a global database index");
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
        tests.Run("Disk dictionaries preserve insertion order, updates and signed key sorting", () => InDirectory(directory =>
        {
            using var collections = new DiskCollections(Path.Combine(directory, "index.sqlite"), 1024 * 1024, 0);
            var records = collections.Create(new Echo.Fusion.GridRecordCodec(), new IntRecordCodec());
            var coordinates = new[] { 8, -2, int.MinValue, 0, int.MaxValue };
            foreach (var x in coordinates)
            {
                records.Add(new Echo.Fusion.Grid(x, 0, 0), x);
            }
            records[new Echo.Fusion.Grid(-2, 0, 0)] = 42;
            Check(records.Select(pair => pair.Key.X).SequenceEqual(coordinates), "Dictionary insertion order changed");
            Check(records[new Echo.Fusion.Grid(-2, 0, 0)] == 42, "Dictionary update failed");
            Check(records.OrderedKeys().Select(key => key.X).SequenceEqual(coordinates.Order()), "Signed sorting changed");
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
            using var work = new WorkBuffers(session, directory, 1024, 0);
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
            using var work = new WorkBuffers(session, directory, 1024 * 1024, 0);
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
