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
        tests.Run("Root compute budgets exceed 512 MiB while nested scopes retain their parent limit", () =>
        {
            var original = compute.MemoryBudgetBytes;
            using (var root = compute.BeginScope(4L << 30))
            {
                Check(compute.MemoryBudgetBytes == 4L << 30, "Root compute budget was silently capped");
                using (var child = compute.BeginScope(8L << 30))
                {
                    Check(child.MemoryBudgetBytes == root.MemoryBudgetBytes, "Nested scope escaped its budget");
                }
            }
            Check(compute.MemoryBudgetBytes == original, "Scope did not restore the previous budget");
        });
        tests.Run("Resident depth planning includes maps and work buffers without exceeding the GPU budget", () =>
        {
            const long depths = 190L * 2048 * 1152;
            var resident = FusionIntegrationPlanning.Resolve(8L << 30, 3L << 30, 31090597514, depths);
            Check(resident.ResidentDepthCount == depths, "Cliff-sized depth maps should fit once in the planned budget");
            Check(resident.CheckpointCapacity >= resident.BatchCapacity * 8, "Disk checkpoints still track individual GPU batches");
            var tiled = FusionIntegrationPlanning.Resolve(8L << 30, 512L << 20, 31090597514, depths);
            Check(tiled.ResidentDepthCount == 0, "Low-memory sessions must use bounded tiles");
        });
        tests.Run("Fusion progress lists preparation, integration and saving with optional cleanup", () =>
        {
            foreach (var cleanup in new[] { false, true })
            {
                var update = FusionProgressPlan.Create(FusionPhase.Integration, cleanup, "working", 500, 1000);
                Check(update.Steps![update.StepIndex!.Value] == "Integrate volume", "The active phase is missing");
                Check(update.Steps.Contains("Clean up islands") == cleanup, "Disabled cleanup must not be a progress step");
                Check(update.Steps[^1] == "Save volume" && update.StepIndex < update.Steps.Length - 1, "Integration must not finish the stage");
            }
        });
        tests.Run("Skipping reused fusion checkpoints preserves the next grid cell", () =>
        {
            var spans = new[] { new GridSpan(1, 2, -100, 100), new GridSpan(3, 4, 7, 5000) };
            var grid = new FusionCandidateGrid(spans, 5195, GridAxis.Z);
            var reader = grid.OpenReader();
            reader.Skip(211);
            var cells = new Grid[5];
            Check(reader.Read(cells) == 5 && cells.SequenceEqual(grid.Skip(211).Take(5)), "Resume skipped or repeated candidate cells");
        });
        tests.Run("Fusion progress reports phase boundaries without duplicate checkpoint completion events", () =>
        {
            var updates = new List<OperationProgress>();
            var progress = new FusionPlanningProgress(updates.Add);
            progress.Start(FusionPhase.Integration, 1000, "grid samples");
            for (var count = 0; count <= 1000; count++)
            {
                progress.Advance(count);
                progress.Advance(count);
            }
            Check(updates[0].Completed == 0 && updates[^1].Completed == 1000, "Throttling lost the first or final update");
            Check(updates.Count(update => update.Completed == 1000) == 1, "Saving a checkpoint repeats its final GPU batch event");
            progress.Start(FusionPhase.Save, 1, "volume");
            progress.Advance(1);
            Check(updates[^2].Completed == 0 && updates[^1].Completed == 1, "The next phase lost its boundaries");
        });
        tests.Run("Grouped fusion checkpoints resume without rebuilding or changing the volume", () =>
        {
            var fixture = new FusionFixture();
            fixture.Project.Settings.ComputeBackend = ComputeBackend.Cpu;
            var chunks = new FusionTestChunkStore();
            var fusion = new DepthFusion(compute);
            var resources = new FusionResourcePlan(25600000, 100000);
            var first = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default, chunks: chunks, grids: chunks);
            var builds = chunks.Builds;
            var resumed = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default, chunks: chunks, grids: chunks);
            Check(builds > 0 && chunks.Builds == builds && chunks.GridBuilds == 1, "Saved integration checkpoints were rebuilt");
            Check(resumed.Samples.SequenceEqual(first.Samples), "Resuming changed the integrated volume");
        });
        tests.Run("Cancellation inside a grouped fusion checkpoint cannot publish partial samples", () =>
        {
            var fixture = Fixture();
            using var stop = new CancellationTokenSource();
            using var integration = new FusionBatchIntegrator(fixture.Views, .125, .01,
                new FusionIntegrationPlan(16, 64), null, ComputeBackend.Cpu, stop.Token);
            var grid = new FusionCandidateGrid([new GridSpan(0, 0, 76, 139)], 64, GridAxis.Z);
            using var group = new FusionChunkIntegrator(() => integration, grid.OpenReader(), 64, stop.Token);
            var store = new FusionTestChunkStore();
            try
            {
                store.ReadOrBuild(0, 64, () => group.Integrate(64, _ => stop.Cancel()));
                throw new InvalidOperationException("Cancelled integration returned a checkpoint");
            }
            catch (OperationCanceledException)
            {
            }
            Check(store.Builds == 0, "A partial group became a reusable checkpoint");
        });
        tests.Run("Volume block encoding matches individual records without reflection or padding", () =>
        {
            var codec = new VolumeRecordCodec();
            var source = Enumerable.Range(0, 4097).Select(index => new VolumeSample
            {
                X = -index,
                Y = int.MaxValue - index,
                Z = index,
                Distance = index * -.03125f,
                Views = index % 190
            }).ToArray();
            var expected = new byte[source.Length * codec.Size];
            for (var index = 0; index < source.Length; index++)
            {
                codec.Encode(source[index], expected.AsSpan(index * codec.Size, codec.Size));
            }
            var actual = new byte[expected.Length];
            codec.EncodeBlock(source, actual);
            var restored = new VolumeSample[source.Length];
            codec.DecodeBlock(actual, restored);
            Check(Unsafe.SizeOf<VolumeSample>() == 20 && actual.SequenceEqual(expected), "Native record layout changed the binary contract");
            Check(restored.SequenceEqual(source), "Block decoding changed records");
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
        tests.Run("CPU and Metal integration preserve evidence across many cameras and partial batches", CompareManyCameras);
        tests.Run("Resident Metal fusion matches tiled evidence and never rereads maps between batches", () =>
        {
            using var scope = compute.BeginScope(16L << 20);
            var fixture = Fixture();
            var counts = fixture.Views.Select(view => new CountingDepthSamples(view.Map.Depth)).ToArray();
            for (var index = 0; index < counts.Length; index++)
            {
                fixture.Views[index].Map.Samples = counts[index];
            }
            var cells = Cells();
            VolumeSample[] expected;
            using (var tiled = new FusionBatchIntegrator(fixture.Views, .125, .01,
                new FusionIntegrationPlan(1024, 64), compute, ComputeBackend.Metal, default))
            {
                cells.CopyTo(tiled.Cells, 0);
                expected = tiled.Integrate(cells.Length).ToArray();
            }
            var before = counts.Sum(value => value.ReadSamples);
            var total = counts.Sum(value => value.Count);
            using var resident = new FusionBatchIntegrator(fixture.Views, .125, .01,
                new FusionIntegrationPlan(1024, 64, total), compute, ComputeBackend.Metal, default);
            Check(counts.Sum(value => value.ReadSamples) - before == total, "Depth maps should load exactly once");
            before = counts.Sum(value => value.ReadSamples);
            cells.CopyTo(resident.Cells, 0);
            for (var repeat = 0; repeat < 3; repeat++)
            {
                Check(resident.Integrate(cells.Length).SequenceEqual(expected), "Resident depth maps changed signed distances or support");
            }
            Check(counts.Sum(value => value.ReadSamples) == before, "Resident integration reread depths");
            resident.Cells[0] = new Grid(0, 0, -80);
            Check(resident.Integrate(1).Count == 0, "An invisible batch returned stale resident results");
        });
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

    private void CompareManyCameras()
    {
        var origin = new V3(6000000, -4000000, 2000000);
        var views = new List<DepthView>();
        for (var index = 0; index < 300; index++)
        {
            var angle = index % 12 * Math.PI / 6;
            var photo = new Photo { Id = index.ToString(), Width = 32, Height = 32, FocalPixels = 32 };
            var camera = new CameraPose
            {
                Centre = (origin + new V3(index % 5 - 2, 0, 0)).Array,
                Rotation = [Math.Cos(angle), 0, Math.Sin(angle), 0, 1, 0, -Math.Sin(angle), 0, Math.Cos(angle)]
            };
            var depths = Enumerable.Repeat(10.03f, 1024).ToArray();
            depths[index % depths.Length] = index % 2 == 0 ? 0 : float.NaN;
            views.Add(new DepthView(photo, camera, new DepthMap { Width = 32, Height = 32, Depth = depths }));
        }
        var cells = new Grid[1025];
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index] = new Grid((int)(origin.X * 8) + (index % 17 - 8) * 8,
                (int)(origin.Y * 8) + (index % 5 - 2) * 8,
                (int)(origin.Z * 8) + (index % 37 - 18) * 8);
        }
        cells[0] = new Grid((int)(origin.X * 8), (int)(origin.Y * 8), (int)(origin.Z * 8) + 80);
        using var integration = new FusionBatchIntegrator(views, .125, .01,
            new FusionIntegrationPlan(cells.Length, 1024, views.Count * 1024), compute, ComputeBackend.Metal, default);
        using var cpu = new FusionBatchIntegrator(views, .125, .01,
            new FusionIntegrationPlan(cells.Length, 32768), null, ComputeBackend.Cpu, default);
        cells.CopyTo(integration.Cells, 0);
        cells.CopyTo(cpu.Cells, 0);
        foreach (var count in new[] { 1, 31, 33, 255, 257, cells.Length })
        {
            var actual = integration.Integrate(count);
            var cpuSamples = cpu.Integrate(count);
            Check(cpuSamples.Count == actual.Count, "CPU block selection changed supported coverage");
            var retained = 0;
            foreach (var cell in cells.AsSpan(0, count))
            {
                var world = new V3(cell.X * .125, cell.Y * .125, cell.Z * .125);
                var value = new FusionAccumulator();
                foreach (var view in views)
                {
                    value.Add(view.Observe(world, .01), view.Focal, .01, .125);
                }
                if (value.Support < 2)
                {
                    continue;
                }
                Check(retained < actual.Count, "Integration removed a supported voxel");
                var sample = actual[retained++];
                Check(sample.X == cell.X && sample.Y == cell.Y && sample.Z == cell.Z && sample.Views == value.Support,
                    "Integration changed ordering or camera support");
                Check(Math.Abs(sample.Distance - value.Sum / value.Weight) < 2e-5, "Integration changed the field");
                var cpuSample = cpuSamples[retained - 1];
                Check(cpuSample.X == sample.X && cpuSample.Y == sample.Y && cpuSample.Z == sample.Z && cpuSample.Views == value.Support,
                    "CPU block selection changed camera evidence");
                Check(cpuSample.Distance == (float)(value.Sum / value.Weight), "CPU block selection changed the reference field");
            }
            Check(retained > 0 && retained == actual.Count, "Integration added unsupported voxels");
        }
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
