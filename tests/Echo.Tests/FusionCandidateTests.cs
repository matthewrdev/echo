using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Tests;

[Export]
public sealed class FusionCandidateTests
{
    [ImportingConstructor]
    public FusionCandidateTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Compressed fusion grids retain 64-bit counts without expanding their cells", () =>
        {
            var ranges = new[] { new GridSpan(1, 2, 0, int.MaxValue), new GridSpan(3, 4, -5, 5) };
            var grid = new FusionCandidateGrid(ranges, (long)int.MaxValue + 12, GridAxis.X);
            Check(grid.Count > int.MaxValue, "Large candidate grids must not overflow a 32-bit counter");
            Check(grid.Take(3).SequenceEqual(new[] { new Grid(0, 1, 2), new Grid(1, 1, 2), new Grid(2, 1, 2) }),
                "Only the requested cells are expanded and restored to world axes");
        });
        tests.Run("Parallel fusion range merging retains every ray cell and extraction neighbour", () => InDirectory(directory =>
        {
            var fixture = RandomCloud();
            var expected = Reference(fixture, .125);
            foreach (var axis in Enum.GetValues<GridAxis>())
            {
                Grid[]? previous = null;
                foreach (var workers in new[] { 1, 4 })
                {
                    using var session = new BulkDataSession(65536, 0);
                    using var work = new WorkBuffers(session, Path.Combine(directory, $"{axis}-{workers}"), 65536, 0);
                    var actual = FusionCandidatePlanner.Build(fixture.Cloud, fixture.Views.ToArray(), .01, .125, 1000000,
                        new FusionGridPlanningOptions(workers, 47, axis), default, work, new FusionPlanningProgress(null));
                    Check(actual != null && actual.Count == expected.Length && actual.ToHashSet().SetEquals(expected),
                        "All cells must match the geometric reference across axes and many tiny runs");
                    var ordered = actual!.ToArray();
                    Check(previous == null || previous.SequenceEqual(ordered), "Worker scheduling must not change output ordering");
                    previous = ordered;
                    Check(work.IndexDiskBytes == 0, "Fusion planning must not open a shared database");
                    Check(!Directory.EnumerateFiles(directory, "run-*.bin", SearchOption.AllDirectories).Any(), "Merged scratch runs must be removed");
                }
            }
        }));
        tests.Run("Fusion planner releases all temporary runs after cancellation and disk exhaustion", () => InDirectory(directory =>
        {
            var fixture = RandomCloud();
            foreach (var diskLimit in new long[] { 0, 1024 })
            {
                using var session = new BulkDataSession(65536, diskLimit);
                using var work = new WorkBuffers(session, Path.Combine(directory, "budget-" + diskLimit), 65536, 0);
                using var cancellation = new CancellationTokenSource();
                var status = new FusionPlanningProgress(update =>
                {
                    if (diskLimit == 0 && update.Operation == "Fusion extraction halo")
                    {
                        cancellation.Cancel();
                    }
                });
                try
                {
                    FusionCandidatePlanner.Build(fixture.Cloud, fixture.Views.ToArray(), .01, .125, 1000000,
                        new FusionGridPlanningOptions(3, 47), cancellation.Token, work, status);
                    throw new InvalidOperationException("Expected the planner to stop");
                }
                catch (OperationCanceledException) when (diskLimit == 0)
                {
                }
                catch (EchoException error) when (diskLimit != 0)
                {
                    Check(error.Message.Contains("scratch-disk budget"), "Disk pressure must remain actionable");
                }
                Check(session.Statistics.ScratchBytes == 0, "Deleting scratch runs must release the shared disk budget");
                Check(!Directory.EnumerateFiles(directory, "run-*.bin", SearchOption.AllDirectories).Any(), "Cancellation must not leak runs");
            }
        }));
        tests.Run("Parallel fusion reports invalid samples without wrapping the useful error", () => InDirectory(directory =>
        {
            var fixture = RandomCloud();
            fixture.Cloud.Samples[0] = new DenseSample(float.NaN, 0, 0, 0, 0, 0, 0, 1);
            using var session = new BulkDataSession(65536, 0);
            using var work = new WorkBuffers(session, directory, 65536, 0);
            try
            {
                FusionCandidatePlanner.Build(fixture.Cloud, fixture.Views.ToArray(), .01, .125, 1000000,
                    new FusionGridPlanningOptions(4, 47), default, work, new FusionPlanningProgress(null));
                throw new InvalidOperationException("Expected invalid point rejection");
            }
            catch (EchoException error)
            {
                Check(error.Message.Contains("non-finite"), "The original failure should reach the UI");
            }
            Check(session.Statistics.ScratchBytes == 0, "Failed parallel workers must release temporary files");
        }));
        tests.Run("Sequential scratch owns only its new files and releases its disk reservation", () => InDirectory(directory =>
        {
            var existing = Path.Combine(directory, "run-0.bin");
            File.WriteAllText(existing, "existing file");
            using var session = new BulkDataSession(65536, 1024);
            using (var records = session.TemporaryRecords(directory, new IntRecordCodec()))
            {
                var file = records.Write(Enumerable.Range(0, 128));
                Check(file.Read().SequenceEqual(Enumerable.Range(0, 128)), "Sequential binary records must round trip");
                Check(session.Statistics.ScratchBytes == 512, "Live scratch must share the session disk budget");
                records.Delete(file);
                Check(session.Statistics.ScratchBytes == 0, "Discarded files must release their budget immediately");
            }
            Check(File.ReadAllText(existing) == "existing file", "Scratch cleanup must not touch pre-existing files");
        }));
        tests.Run("Batched paged records survive page boundaries, eviction and reopening", () => InDirectory(directory =>
        {
            var source = Enumerable.Range(0, 20003).Select(index => new DenseSample(index, -index, index * .25f, 1, 2, 3, index % 2, .75f)).ToArray();
            var path = Path.Combine(directory, "cloud.bin");
            using (var session = new BulkDataSession(65536, 0))
            {
                var list = session.Open(path, new DenseRecordCodec(), true);
                list.Add(source[0]);
                list.AddRange(source.AsSpan(1));
                var actual = new DenseSample[17001];
                list.ReadRange(2001, actual);
                Check(actual.SequenceEqual(source.Skip(2001).Take(actual.Length)), "Batched reads must include dirty pages and straddling records");
            }
            using var reopened = new BulkDataSession(65536, 0);
            var saved = reopened.Open(path, new DenseRecordCodec());
            var restored = new DenseSample[source.Length];
            saved.ReadRange(0, restored);
            Check(restored.SequenceEqual(source), "Batched append must persist the count and every encoded field");
        }));
    }

    private FusionFixture RandomCloud()
    {
        var fixture = new FusionFixture();
        fixture.Cloud.Samples.Clear();
        var random = new Random(917);
        for (var index = 0; index < 600; index++)
        {
            var view = index % 2;
            var point = fixture.Views[view].Point(random.Next(12, 52), random.Next(12, 52), random.Next(5, 16));
            var sample = new DenseSample((float)point.X, (float)point.Y, (float)point.Z, 0, 0, 0, view, 1);
            fixture.Cloud.Samples.Add(sample);
            fixture.Cloud.Samples.Add(sample);
        }
        return fixture;
    }

    private Grid[] Reference(FusionFixture fixture, double voxel)
    {
        var occupied = new HashSet<Grid>();
        foreach (var point in fixture.Cloud.Samples)
        {
            var view = fixture.Views[point.View];
            var world = new V3(point.X, point.Y, point.Z);
            var ray = world - view.Centre;
            var depth = CameraGeometry.Rotate(view.Inverse, ray).Z;
            var extent = ray * (Math.Max(depth * .01, depth / view.Focal * 2) / depth);
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(extent.X), Math.Max(Math.Abs(extent.Y), Math.Abs(extent.Z))) / voxel));
            for (var step = -steps; step <= steps; step++)
            {
                var sample = world + extent / steps * step;
                occupied.Add(new Grid((int)Math.Floor(sample.X / voxel), (int)Math.Floor(sample.Y / voxel), (int)Math.Floor(sample.Z / voxel)));
            }
        }
        var result = new HashSet<Grid>();
        foreach (var cell in occupied)
        {
            for (var x = -1; x <= 2; x++)
            {
                for (var y = -1; y <= 2; y++)
                {
                    for (var z = -1; z <= 2; z++)
                    {
                        result.Add(new Grid(cell.X + x, cell.Y + y, cell.Z + z));
                    }
                }
            }
        }
        return result.OrderBy(cell => cell.X).ThenBy(cell => cell.Y).ThenBy(cell => cell.Z).ToArray();
    }

    private void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-grid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
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
