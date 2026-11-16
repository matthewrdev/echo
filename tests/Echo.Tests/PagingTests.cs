using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Fusion;
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
