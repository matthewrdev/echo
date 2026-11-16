using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Infrastructure;

namespace Echo.Tests;

[Export]
public sealed class FusionSettingsTests
{
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public FusionSettingsTests(IJsonCodec json)
    {
        this.json = json;
    }

    internal void Run(TestSuite tests)
    {
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
            foreach (var spacing in new[] { -1, double.NaN, double.PositiveInfinity })
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
