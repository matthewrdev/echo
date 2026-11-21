using Echo.Contracts;
using Echo.Fusion;
using Echo.Geometry;
using Echo.Infrastructure;

namespace Echo.Tests;

public sealed class FusionSettingsTests
{
    private readonly IJsonCodec json;
    private readonly IFusionResourcePlanner resourcePlanner;

    public FusionSettingsTests(IJsonCodec json, IFusionResourcePlanner resourcePlanner)
    {
        this.json = json;
        this.resourcePlanner = resourcePlanner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Fusion shares the run budget without overriding smaller explicit limits", () =>
        {
            const long megabyte = 1024 * 1024;
            foreach (var requested in new[] { 0, 4096 })
            {
                var plan = resourcePlanner.Plan(new Settings { FusionMemoryBudgetMb = requested }, 512 * megabyte);
                Check(plan.MemoryBudgetBytes == 512 * megabyte, "Fusion independently claimed memory reserved by the run");
                Check(plan.MaximumGridSamples * 256L == plan.MemoryBudgetBytes, "Candidate planning exceeded the shared budget");
            }

            var limited = resourcePlanner.Plan(new Settings { FusionMemoryBudgetMb = 256 }, 512 * megabyte);
            Check(limited.MemoryBudgetBytes == 256 * megabyte, "An explicit smaller fusion budget was ignored");
        });
        tests.Run("Explicit fusion budgets support 16, 32 and 64 GiB without automatic clamping", () =>
        {
            foreach (var megabytes in new[] { 16384, 32768, 65536 })
            {
                var plan = FusionMemoryBudget.Resolve(megabytes, 64L * 1024 * 1024 * 1024);
                Check(plan.MemoryBudgetBytes == megabytes * 1024L * 1024, "The requested working budget is respected");
                Check(plan.MaximumGridSamples * 256L == plan.MemoryBudgetBytes, "The sample limit uses the explicit budget");
            }
        });
        tests.Run("Fusion preserves exact voxel spacing independently of the detail multiplier", () =>
        {
            var fixture = new FusionFixture();
            fixture.Project.Settings.FusionVoxelSize = .25;
            fixture.Project.Settings.FusionDetailMultiplier = 16;
            var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings,
                new FusionResourcePlan(25600000, 100000), default);
            Check(grid.VoxelSize == .25 && grid.RequestedVoxelSize == .25, "Explicit spacing overrides automatic resolution");
        });
        tests.Run("Fusion stops at an insufficient budget unless coarsening is explicitly enabled", () =>
        {
            var fixture = new FusionFixture();
            var resources = new FusionResourcePlan(256000, 1000);
            fixture.Project.Settings.FusionVoxelSize = .125;
            try
            {
                FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default);
                throw new InvalidOperationException("The default must not silently reduce detail");
            }
            catch (EchoException exception)
            {
                Check(exception.Message.Contains("Resolution was not reduced") && exception.Message.Contains("FusionMemoryBudgetMb"),
                    "The failure explains what happened and how to resolve it");
            }

            fixture.Project.Settings.FusionAllowCoarserResolution = true;
            var messages = new List<string>();
            var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default, messages.Add);
            Check(grid.VoxelSize > grid.RequestedVoxelSize && grid.Keys.Count <= resources.MaximumGridSamples,
                "The explicit fallback retains complete coverage within the budget");
            Check(messages.Any(message => message.Contains("Coarser resolution is enabled")), "Every fallback is visible in progress logs");
        });
        tests.Run("Automatic fusion respects the 2.5 cm floor and explicit spacing remains exact", () =>
        {
            var fixture = new FusionFixture();
            fixture.Cloud.Samples.Clear();
            fixture.Cloud.Samples.Add(new DenseSample(-.25f, 0, 10, 0, 0, 0, 0, 1));
            fixture.Views.Clear();
            for (var index = 0; index < fixture.Project.Images.Count; index++)
            {
                var photo = fixture.Project.Images[index];
                photo.FocalPixels = 6400;
                fixture.Views.Add(new DepthView(photo, fixture.Scene.Cameras[index], fixture.Maps[index]));
            }
            fixture.Project.Settings.FusionDetailMultiplier = 16;
            var resources = new FusionResourcePlan(25600000, 100000);
            foreach (var target in new[] { 0, .025, .1, .5 })
            {
                fixture.Project.Settings.FusionVoxelSize = target;
                fixture.Project.Settings.Validate();
                var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default);
                var expected = target == 0 ? .025 : target;
                Check(grid.RequestedVoxelSize == expected && grid.VoxelSize == expected, "Fusion did not respect the target or minimum spacing");
            }
            fixture.Project.Settings.FusionVoxelSize = .024999;
            try
            {
                FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default);
                throw new InvalidOperationException("Grid planning accepted voxels below 2.5 cm");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        });
        tests.Run("Automatic paged fusion obeys its candidate budget before integration", () =>
        {
            var fixture = new FusionFixture();
            var directory = Path.Combine(Path.GetTempPath(), "echo-fusion-budget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var data = new Echo.Storage.BulkDataSession(65536, 0);
                var work = new Echo.Storage.WorkBuffers(data, directory);
                var grid = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings,
                    new FusionResourcePlan(256000, 1000), default, work: work);
                Check(grid.Keys.Count <= 1000 && grid.VoxelSize > grid.RequestedVoxelSize, "Paged storage bypassed the automatic voxel budget");
                foreach (var target in new[] { .1, .5 })
                {
                    fixture.Project.Settings.FusionVoxelSize = target;
                    var exact = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings,
                        new FusionResourcePlan(256000, 1000), default, work: work);
                    Check(exact.VoxelSize == target, "Explicit centimetre targets must not be silently changed");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
        tests.Run("Fusion resource settings round trip and reject invalid budgets and spacing", () =>
        {
            var original = new Settings { FusionMemoryBudgetMb = 32768, FusionVoxelSize = .05, FusionAllowCoarserResolution = true };
            var restored = json.Clone(original);
            restored.Validate();
            Check(restored.FusionMemoryBudgetMb == 32768 && restored.FusionVoxelSize == .05 && restored.FusionAllowCoarserResolution,
                "CLI settings preserve memory, resolution and fallback policy");
            foreach (var megabytes in new[] { -1, 1, 63, 524288 })
            {
                Invalid(new Settings { FusionMemoryBudgetMb = megabytes });
            }
            foreach (var spacing in new[] { -1, .001, .024999, double.NaN, double.PositiveInfinity })
            {
                Invalid(new Settings { FusionVoxelSize = spacing });
            }
        });
    }

    private void Invalid(Settings settings)
    {
        try
        {
            settings.Validate();
        }
        catch (EchoException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid fusion settings were accepted");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
