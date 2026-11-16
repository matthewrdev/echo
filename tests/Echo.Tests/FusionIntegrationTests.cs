using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Echo.Compute;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Tests;

[Export]
public sealed class FusionIntegrationTests
{
    private readonly IComputeService compute;

    [ImportingConstructor]
    public FusionIntegrationTests(IComputeService compute)
    {
        this.compute = compute;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Fusion integration budgets adapt to small scenes and select Metal only for large automatic batches", () =>
        {
            var small = FusionIntegrationPlanning.Resolve(8L << 30, 512L << 20, 1000, 8192);
            Check(small.BatchCapacity == 1024 && small.DepthCapacity == 8192, "Small scenes reserve oversized buffers");
            Check(small.SelectBackend(ComputeBackend.Auto) == ComputeBackend.Cpu, "Small automatic batches should use CPU");
            Check(small.SelectBackend(ComputeBackend.Metal) == ComputeBackend.Metal, "Explicit Metal was overridden");
            var large = FusionIntegrationPlanning.Resolve(8L << 30, 512L << 20);
            Check(large.BatchCapacity == 262144 && large.SelectBackend(ComputeBackend.Auto) == ComputeBackend.Auto, "Large scenes lost GPU eligibility");
        });
        tests.Run("Fusion volume records contain no managed references and retain the explicit binary layout", () =>
        {
            Check(!RuntimeHelpers.IsReferenceOrContainsReferences<VolumeSample>(), "Voxel records must be unmanaged values");
            var codec = new VolumeRecordCodec();
            Span<byte> bytes = stackalloc byte[20];
            var sample = new VolumeSample { X = -17, Y = 19, Z = 2, Distance = -.125f, Views = 7 };
            codec.Encode(sample, bytes);
            Check(codec.Size == 20 && codec.Decode(bytes).Equals(sample), "Binary contract changed");
        });
        tests.Run("Tiled CPU fusion preserves full-camera evidence across split windows and empty batches", () => Compare(ComputeBackend.Cpu));
        tests.Run("Fusion grid span reads cross buffer boundaries and Int32 endpoints without changing order", () =>
        {
            foreach (var axis in Enum.GetValues<GridAxis>())
            {
                var ranges = Enumerable.Range(0, 4100).Select(index => new GridSpan(index, -2, -1, 1)).ToList();
                ranges.Add(new GridSpan(5000, 0, int.MaxValue - 2, int.MaxValue));
                var grid = new FusionCandidateGrid(ranges, 12303, axis);
                var expected = grid.ToArray();
                var reader = grid.OpenReader();
                var buffer = new Grid[137];
                var actual = new List<Grid>();
                int count;
                while ((count = reader.Read(buffer)) > 0)
                {
                    actual.AddRange(buffer.AsSpan(0, count).ToArray());
                }
                Check(actual.SequenceEqual(expected), "Span reader skipped or repeated cells");
            }
        });
        tests.Run("Fusion depth windows retain observations at tile edges, near the camera plane and across invalid pixels", () =>
        {
            var fixture = Fixture();
            var cells = Cells();
            var bounds = FusionBoundsBuilder.Measure(cells, .125);
            using var tiles = new FusionDepthTiles(64, default);
            tiles.Start(fixture.Views, bounds, default);
            var actual = new DepthObservation[fixture.Views.Count, cells.Length];
            var hits = new int[fixture.Views.Count, cells.Length];
            while (tiles.LoadNext())
            {
                Check(tiles.Length <= 64, "Depth upload exceeded capacity");
                foreach (var window in tiles.Windows.AsSpan(0, tiles.Count))
                {
                    var view = fixture.Views.IndexOf(window.View);
                    for (var index = 0; index < cells.Length; index++)
                    {
                        var cell = cells[index];
                        var value = window.Observe(new V3(cell.X * .125, cell.Y * .125, cell.Z * .125), .01, tiles.Depths);
                        if (value.IsValid)
                        {
                            actual[view, index] = value;
                            hits[view, index]++;
                        }
                    }
                }
            }
            for (var view = 0; view < fixture.Views.Count; view++)
            {
                for (var index = 0; index < cells.Length; index++)
                {
                    var cell = cells[index];
                    var expected = fixture.Views[view].Observe(new V3(cell.X * .125, cell.Y * .125, cell.Z * .125), .01);
                    var value = actual[view, index];
                    Check(hits[view, index] == (expected.IsValid ? 1 : 0), "Culling lost an observation or a tile voted twice");
                    Check(value.Distance == expected.Distance && value.HasFullCoverage == expected.HasFullCoverage, "Bilinear evidence changed");
                }
            }
        });
        tests.Run("Fusion block projection conservatively crops rotated cameras at large world origins", () =>
        {
            var origin = new V3(6000000, -4000000, 2000000);
            foreach (var angle in new[] { 0, .3, 1.5, Math.PI })
            {
                var cosine = Math.Cos(angle);
                var sine = Math.Sin(angle);
                var photo = new Photo { Id = "rotated", Width = 64, Height = 64, FocalPixels = 64 };
                var camera = new CameraPose
                {
                    ImageId = photo.Id,
                    Centre = origin.Array,
                    Rotation = [cosine, 0, sine, 0, 1, 0, -sine, 0, cosine]
                };
                var map = new DepthMap { ImageId = photo.Id, Width = 64, Height = 64, Depth = Enumerable.Repeat(10f, 4096).ToArray() };
                var view = new DepthView(photo, camera, map);
                foreach (var local in new[] { new V3(0, 0, 10), new V3(4.8, 0, 10), new V3(-4.8, 0, 10),
                    new V3(1000, 0, 10), new V3(0, 0, -10), new V3(0, 0, 0) })
                {
                    var centre = origin + CameraGeometry.Rotate(camera.Rotation, local);
                    var radius = new V3(.5, .5, .5);
                    var region = FusionDepthProjection.Project(view, new FusionBounds(centre - radius, centre + radius), origin);
                    if (local.X == 0 && local.Z == 10)
                    {
                        Check(region.Count > 0 && region.Count < map.SampleCount / 4, "Central block did not crop the camera");
                    }
                    var depths = new float[region.Count];
                    for (var row = 0; row < region.Height; row++)
                    {
                        map.ReadDepth((region.Top + row) * map.Width + region.Left, depths.AsSpan(row * region.Width, region.Width));
                    }
                    var window = new FusionDepthWindow(view, region, 0);
                    for (var x = -1; x <= 1; x++)
                    {
                        for (var y = -1; y <= 1; y++)
                        {
                            for (var z = -1; z <= 1; z++)
                            {
                                var world = centre + new V3(x * .5, y * .5, z * .5);
                                var actual = window.Observe(world, .01, depths);
                                var expected = view.Observe(world, .01);
                                var sameDepth = actual.Distance == expected.Distance && actual.CameraDepth == expected.CameraDepth;
                                Check(sameDepth && actual.HasFullCoverage == expected.HasFullCoverage, "Cropping changed camera evidence");
                            }
                        }
                    }
                }
            }
        });
        tests.Run("Cancelled integration cannot publish a partial pooled batch", () =>
        {
            var fixture = Fixture();
            using var stop = new CancellationTokenSource();
            using var integration = new FusionBatchIntegrator(fixture.Views, .125, .01, new FusionIntegrationPlan(1024, 64), null, ComputeBackend.Cpu, stop.Token);
            stop.Cancel();
            try
            {
                integration.Integrate(1);
                throw new InvalidOperationException("Cancelled batch returned");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    internal void RunMetal(TestSuite tests)
    {
        using (var probe = compute.Open(ComputeBackend.Auto, default))
        {
            if (probe == null)
            {
                Console.WriteLine("SKIP tiled fusion Metal checks: Metal is unavailable");
                return;
            }
        }
        tests.Run("Metal fusion accumulates split depth uploads within a budget smaller than all maps", () =>
        {
            using var scope = compute.BeginScope(65536);
            Compare(ComputeBackend.Metal);
            Check(scope.Devices.Length == 1, "Tiled integration silently fell back to CPU");
        });
        tests.Run("Automatic fusion allocation failure restarts safely on CPU", () =>
        {
            using var scope = compute.BeginScope(16);
            Compare(ComputeBackend.Auto);
        });
        tests.Run("Automatic fusion discards partial GPU sums after a later upload fails", () =>
            Compare(ComputeBackend.Auto, new FailingFusionComputeService(compute)));
        tests.Run("Metal fusion gives the same field across depth tile capacities", () =>
        {
            var fixture = Fixture();
            var cells = Cells();
            VolumeSample[] Execute(int capacity)
            {
                using var integration = new FusionBatchIntegrator(fixture.Views, .125, .01,
                    new FusionIntegrationPlan(1024, capacity), compute, ComputeBackend.Metal, default);
                cells.CopyTo(integration.Cells, 0);
                return integration.Integrate(cells.Length).ToArray();
            }
            Check(Execute(64).SequenceEqual(Execute(32768)), "Splitting camera uploads altered the GPU field");
        });
    }

    private void Compare(ComputeBackend backend, IComputeService? service = null)
    {
        var fixture = Fixture();
        var cells = Cells();
        using var integration = new FusionBatchIntegrator(fixture.Views, .125, .01,
            new FusionIntegrationPlan(1024, 64), service ?? compute, backend, default);
        cells.CopyTo(integration.Cells, 0);
        var actual = integration.Integrate(cells.Length).ToArray();
        var expected = new List<VolumeSample>();
        foreach (var cell in cells)
        {
            var world = new V3(cell.X * .125, cell.Y * .125, cell.Z * .125);
            var value = new FusionAccumulator();
            foreach (var view in fixture.Views)
            {
                value.Add(view.Observe(world, .01), view.Focal, .01, .125);
            }
            if (value.Support >= 2)
            {
                expected.Add(new VolumeSample { X = cell.X, Y = cell.Y, Z = cell.Z, Distance = (float)(value.Sum / value.Weight), Views = value.Support });
            }
        }
        Check(expected.Count > 0 && actual.Length == expected.Count, "Supported coverage differs");
        for (var index = 0; index < actual.Length; index++)
        {
            var a = actual[index];
            var e = expected[index];
            Check(a.X == e.X && a.Y == e.Y && a.Z == e.Z && a.Views == e.Views, "Ordering or support differs");
            Check(Math.Abs(a.Distance - e.Distance) < 2e-5, "Signed distance differs");
        }
        integration.Cells[0] = new Grid(0, 0, -80);
        Check(integration.Integrate(1).Count == 0, "Empty batch leaked previous GPU state");
    }

    private FusionFixture Fixture()
    {
        var fixture = new FusionFixture();
        foreach (var depth in new[] { 7f, 10.03f, 11f, 0f, float.NaN })
        {
            var id = "additional-" + fixture.Views.Count;
            var photo = new Photo { Id = id, Width = 64, Height = 64, FocalPixels = 64 };
            var camera = new CameraPose { ImageId = id, Centre = [0, 0, 0] };
            var map = new DepthMap { ImageId = id, Width = 64, Height = 64, Depth = Enumerable.Repeat(depth, 4096).ToArray() };
            fixture.Views.Add(new DepthView(photo, camera, map));
        }
        fixture.Maps[0].Depth[32 * 64 + 32] = 0;
        fixture.Maps[1].Depth[31 * 64 + 31] = 20;
        return fixture;
    }

    private Grid[] Cells()
    {
        var cells = new List<Grid> { new(0, 0, -1), new(0, 0, 0), new(0, 0, 1), new(10000, 0, 80) };
        for (var z = 76; z <= 84; z++)
        {
            for (var x = -32; x <= 32; x += 4)
            {
                for (var y = -32; y <= 32; y += 16)
                {
                    cells.Add(new Grid(x, y, z));
                }
            }
        }
        return cells.ToArray();
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
