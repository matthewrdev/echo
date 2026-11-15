using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Meshing;
using Grid = Echo.Fusion.Grid;

namespace Echo.Tests;

[Export]
public sealed class FusionQualityTests
{
    private readonly IDepthFusion fusion;
    private readonly IMeshBuilder meshes;

    [ImportingConstructor]
    public FusionQualityTests(IDepthFusion fusion, IMeshBuilder meshes)
    {
        this.fusion = fusion;
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
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
            Check(volume.VoxelSize < .013, "Fine source resolution is retained");
            Check(volume.Samples.Any(s => s.Distance > 0) && volume.Samples.Any(s => s.Distance < 0), "Both sides of the jointly supported surface survive");
            var mesh = meshes.Extract(volume, default);
            Check(mesh.Vertices.All(p => Math.Abs(p[2] - 10) < .002), "Integration recovers the shared surface instead of separate noise layers");
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
            var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, new FusionResourcePlan(256000, 1000), default);
            Check(grid.Keys.Length <= 1000 && grid.VoxelSize > grid.RequestedVoxelSize, "The actual candidate count obeys the limit");
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
            foreach (var detail in new[] { .25, 1, 16 })
            {
                var plan = FusionMemoryBudget.Resolve(detail, capacity);
                Check(plan.MemoryBudgetBytes <= capacity / 4, "Fusion scratch leaves room for source maps and cloud");
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

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
