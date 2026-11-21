using Echo.Fusion;
using Echo.Geometry;
using Echo.Meshing.Volumes;
using Echo.Meshing;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Resources;
using Echo.Storage;
using Grid = Echo.Fusion.Grid;

namespace Echo.Tests;

public sealed class FusionQualityTests
{
    private readonly IDepthFusion fusion;
    private readonly IMeshBuilder meshes;

    public FusionQualityTests(IDepthFusion fusion, IMeshBuilder meshes)
    {
        this.fusion = fusion;
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Fusion weights depth precision across near and distant capture passes", () =>
        {
            var fixture = new FusionFixture();
            for (var index = 0; index < 6; index++)
            {
                AddPlane(fixture, "distant-" + index, 98, 0);
                fixture.Scene.Cameras[^1].Centre = [0, 0, -90];
            }

            var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                new FusionResourcePlan(25600000, 100000), default);
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.Count > 0 && mesh.Vertices.All(point => Math.Abs(point[2] - 10) < .25),
                "Two close views constrain the surface despite six less precise distant estimates of a foreground layer");
        });
        tests.Run("Fusion removes a ghost layer contradicted by observed free space", () =>
        {
            var fixture = new FusionFixture();
            foreach (var map in fixture.Maps)
            {
                Array.Fill(map.Depth, 8f);
            }

            for (var view = 0; view < 4; view++)
            {
                AddPlane(fixture, "background-" + view, 10, (view - 1.5) * .2);
            }

            var original = fixture.Cloud.Samples.ToArray();
            foreach (var point in original)
            {
                fixture.Cloud.Samples.Add(new DenseSample(point.X, point.Y, 8, 0, 0, 0, 0, 1));
            }

            var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                new FusionResourcePlan(25600000, 100000), default);
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.Count > 0, "The measured background surface remains reconstructable");
            Check(mesh.Vertices.All(point => Math.Abs(point[2] - 10) < 1e-6), "Two erroneous foreground views cannot create a hull across four observed free-space rays");
        });
        tests.Run("Fusion visibility evidence excludes missing depths and depth discontinuities", () =>
        {
            var fixture = new FusionFixture();
            var view = fixture.Views[0];
            var point = view.Point(31, 31, 8);
            var observed = view.Observe(point, .01);
            Check(observed.IsValid && observed.HasFullCoverage && observed.Distance == 2, "Continuous depth establishes empty space before its surface");
            Check(view.Observe(view.Point(31, 31, 12), .01).Distance == -2, "A closer surface reports occlusion, not free space");
            view.Map.Depth[32 * 64 + 32] = 0;
            var partial = view.Observe(point, .01);
            Check(partial.IsValid && !partial.HasFullCoverage, "A missing interpolation corner cannot carve a surface");
            view.Map.Depth[32 * 64 + 32] = 20;
            Check(!view.Observe(point, .01).IsValid, "A foreground/background boundary cannot invent an intermediate surface");
            Array.Fill(view.Map.Depth, float.NaN);
            Check(!view.Observe(point, .01).IsValid, "Non-finite depths provide no visibility evidence");
            Check(!view.Observe(new V3(0, 0, -1), .01).IsValid, "Points behind the camera provide no evidence");
            Check(!view.Observe(new V3(1e100, 0, 1), .01).IsValid, "Unprojectable coordinates are rejected before integer conversion");
        });
        tests.Run("Occluded and unknown depth views do not erase an observed fusion surface", () =>
        {
            var fixture = new FusionFixture();
            AddPlane(fixture, "occluder", 7, 0);
            AddPlane(fixture, "missing", 0, 0);
            var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                new FusionResourcePlan(25600000, 100000), default);
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.All(point => Math.Abs(point[2] - 10) < 1e-6), "Only observable rays constrain the zero crossing");
        });
        tests.Run("Fusion island trimming removes whole regions before extraction and preserves the retained field", () =>
        {
            var volume = new FusedVolume { VoxelSize = .5, InputSamples = 999, ConsistentSamples = 800 };
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    for (var z = 0; z < 3; z++)
                    {
                        volume.Samples.Add(new VolumeSample { X = x, Y = y, Z = z, Distance = z - .25f, Views = 2 });
                    }
                }
            }

            var main = meshes.Extract(volume, default);
            var original = volume.Samples.ToArray();
            volume.Samples.Add(new VolumeSample { X = 50, Y = 50, Z = 50, Distance = -.1f, Views = 3 });
            volume.Samples.Add(new VolumeSample { X = 51, Y = 51, Z = 51, Distance = .1f, Views = 3 });
            var result = FusionIslandTrimmer.Trim(volume, IslandTrimMode.Small, 2, default);
            Check(result.InputRegions == 2 && result.RemovedRegions == 1 && result.RemovedSamples == 2, "Diagonal neighbours form one region; separated floaters are removed");
            Check(result.Volume.Samples.SequenceEqual(original), "The retained signed distances, support counts and ordering are exact");
            Check(result.Volume.InputSamples == 999 && result.Volume.ConsistentSamples == 800, "Input provenance remains intact");
            var after = meshes.Extract(result.Volume, default);
            Check(after.Faces.Count == main.Faces.Count && after.Vertices.Count == main.Vertices.Count, "Removing an island creates no extraction holes in the main surface");
            Check(volume.Samples.Count == original.Length + 2, "The source volume is not modified");
            Check(ReferenceEquals(FusionIslandTrimmer.Trim(volume, IslandTrimMode.Off, 100, default).Volume, volume), "Off preserves all regions");
            Check(FusionIslandTrimmer.Trim(volume, IslandTrimMode.Small, 0, default).RemovedSamples == 0, "Zero threshold preserves every region");
            Check(FusionIslandTrimmer.Trim(volume, IslandTrimMode.Main, 0, default).RemovedSamples == 2, "Main-region mode ignores the small-region threshold");
            Check(FusionIslandTrimmer.Trim(new FusedVolume(), IslandTrimMode.Main, .1, default).InputRegions == 0, "Empty volumes are handled");
            try
            {
                FusionIslandTrimmer.Trim(volume, IslandTrimMode.Main, .1, new CancellationToken(true));
                throw new InvalidOperationException("Expected cancelled trimming");
            }
            catch (OperationCanceledException)
            {
            }
        });
        tests.Run("Fusion labels preserve first-seen ties and connectivity when scratch buffers spill", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-fusion-labels-" + Guid.NewGuid().ToString("N"));
            try
            {
                var volume = new FusedVolume { VoxelSize = .1 };
                for (var index = 4999; index >= 0; index--)
                {
                    volume.Samples.Add(new VolumeSample { X = index, Y = index, Z = index, Distance = index * .01f, Views = 3 });
                    volume.Samples.Add(new VolumeSample { X = index, Y = index, Z = index + 10000, Distance = index * -.01f, Views = 4 });
                }

                var expected = FusionIslandTrimmer.Trim(volume, IslandTrimMode.Main, 0, default);
                Check(expected.InputRegions == 2 && expected.RemovedSamples == 5000 && expected.Volume.Samples[0].Z == 4999,
                    "Equal-sized regions retain the first input region, including reverse input order and diagonal connectivity");
                using var session = new BulkDataSession(65536 * 4, 0);
                using var work = new WorkBuffers(session, directory);
                var actual = FusionIslandTrimmer.Trim(volume, IslandTrimMode.Main, 0, default, work);
                Check(actual.Volume.Samples.SequenceEqual(expected.Volume.Samples) && actual.RemovedRegions == expected.RemovedRegions,
                    "Spilling connectivity buffers changed retained sample values or ordering");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        });
        tests.Run("Fusion resolution is independent of the final triangle budget", () =>
        {
            var fixture = new FusionFixture();
            var resources = new FusionResourcePlan(64 * 1024 * 1024, 100000);
            fixture.Project.Settings.FaceLimit = 1;
            var first = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
            fixture.Project.Settings.FaceLimit = 10000000;
            var second = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud, resources, default);
            Check(first.VoxelSize == .125 && second.VoxelSize == first.VoxelSize, "A 15.625 cm footprint produces the same 12.5 cm grid at either face limit");
            Check(first.Samples.Count == second.Samples.Count, "Triangle budgets cannot alter fusion coverage");
            for (var index = 0; index < first.Samples.Count; index++)
            {
                var a = first.Samples[index];
                var b = second.Samples[index];
                Check(a.X == b.X && a.Y == b.Y && a.Z == b.Z, "Stable sample ordering");
                Check(a.Distance == b.Distance && a.Views == b.Views, "Stable observed field");
            }

            Check(first.Samples.All(s => s.Views == 2 && float.IsFinite(s.Distance)), "Only supported finite samples are retained");
            var mesh = meshes.Extract(first, default);
            Check(mesh.Vertices.All(p => Math.Abs(p[2] - 10) < 1e-6), "The fused zero crossing stays on the measured plane");
        });
        tests.Run("Fine fusion retains noisy depths that passed cloud agreement instead of opening holes", () =>
        {
            var fixture = new FusionFixture();
            foreach (var photo in fixture.Project.Images)
            {
                photo.FocalPixels = 640;
            }

            Array.Fill(fixture.Maps[0].Depth, 9.955f);
            Array.Fill(fixture.Maps[1].Depth, 10.045f);
            fixture.Cloud.Samples.Clear();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                {
                    fixture.Cloud.Samples.Add(new DenseSample(x * .025f, y * .025f, 9.955f, 0, 0, 0, 0, 1));
                }
            }

            var volume = fusion.Fuse(fixture.Project, fixture.Scene, fixture.Maps, fixture.Cloud,
                new FusionResourcePlan(25600000, 100000), default);
            Check(volume.VoxelSize == .025, "Fine source resolution stops at the 2.5 cm minimum");
            Check(volume.Samples.Any(s => s.Distance > 0) && volume.Samples.Any(s => s.Distance < 0), "Both sides of the jointly supported surface survive");
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.Count > 0 && mesh.Vertices.All(p => Math.Abs(p[2] - 10) < .002),
                "Integration recovers the shared surface instead of separate noise layers");
        });
        tests.Run("Fusion footprint uses verified surfaces rather than rejected background depths", () =>
        {
            var fixture = new FusionFixture();
            foreach (var map in fixture.Maps)
            {
                Array.Fill(map.Depth, 10000f);
            }

            Check(FusionFootprint.Measure(fixture.Views, fixture.Cloud, default) == 10.0 / 64, "Background pixels do not coarsen the surface grid");
            fixture.Cloud.Samples[0] = new DenseSample(0, 0, .0001f, 0, 0, 0, 0, 1);
            Check(FusionFootprint.Measure(fixture.Views, fixture.Cloud, default) == 10.0 / 64, "A near-camera outlier does not dictate grid resolution");
        });
        tests.Run("Fusion memory limits coarsen the complete grid instead of discarding coverage", () =>
        {
            var fixture = new FusionFixture();
            fixture.Project.Settings.FusionAllowCoarserResolution = true;
            var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, new FusionResourcePlan(256000, 1000), default);
            Check(grid.Keys.Count <= 1000 && grid.VoxelSize > grid.RequestedVoxelSize, "The actual candidate count obeys the limit");
            var keys = grid.Keys.ToHashSet();
            foreach (var point in fixture.Cloud.Samples)
            {
                var x = (int)Math.Floor(point.X / grid.VoxelSize);
                var y = (int)Math.Floor(point.Y / grid.VoxelSize);
                var z = (int)Math.Floor(point.Z / grid.VoxelSize);
                Check(keys.Contains(new Grid(x - 1, y - 1, z - 1)) && keys.Contains(new Grid(x + 2, y + 2, z + 2)), "Every observed cell retains its extraction halo, including negative coordinates");
            }
        });
        tests.Run("Fusion detail increases resolution while resource estimates remain bounded", () =>
        {
            var fixture = new FusionFixture();
            var resources = new FusionResourcePlan(25600000, 100000);
            var normal = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default);
            var detailed = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, new Settings { FusionDetailMultiplier = 4 }, resources, default);
            Check(detailed.VoxelSize == normal.VoxelSize * .5, "Higher detail halves spacing when memory permits");
            var capacity = 8L * 1024 * 1024 * 1024;
            foreach (var capacityMultiplier in new[] { 1, 4, 8 })
            {
                var available = capacity * capacityMultiplier;
                var plan = FusionMemoryBudget.Resolve(0, available);
                Check(plan.MemoryBudgetBytes == available - available / 8, "Automatic fusion retains system headroom without a small fixed ceiling");
                Check(plan.MaximumGridSamples * 256L <= plan.MemoryBudgetBytes, "The candidate limit matches the memory estimate");
            }
        });
        tests.Run("Fusion does not invent jointly observed volume and planning respects cancellation", () =>
        {
            var fixture = new FusionFixture();
            var resources = new FusionResourcePlan(25600000, 100000);
            try
            {
                fusion.Fuse(fixture.Project, fixture.Scene, [fixture.Maps[0]], fixture.Cloud, resources, default);
                throw new InvalidOperationException("Single-view data must not pass joint observation checks");
            }
            catch (EchoException)
            {
            }

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, cancellation.Token);
                throw new InvalidOperationException("Expected cancelled planning");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void AddPlane(FusionFixture fixture, string id, float depth, double offset)
    {
        fixture.Project.Images.Add(new Photo { Id = id, Width = 64, Height = 64, FocalPixels = 64 });
        fixture.Scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [offset, 0, 0] });
        fixture.Maps.Add(new DepthMap { ImageId = id, Width = 64, Height = 64, Depth = Enumerable.Repeat(depth, 64 * 64).ToArray() });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
