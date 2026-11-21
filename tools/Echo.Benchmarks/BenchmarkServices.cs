using Microsoft.Extensions.DependencyInjection;

namespace Echo.Benchmarks;

public sealed class BenchmarkServices
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<AlignmentBenchmark>();
        services.AddSingleton<BundleBenchmark>();
        services.AddSingleton<CloudBenchmark>();
        services.AddSingleton<CloudComparison>();
        services.AddSingleton<CloudPublicationBenchmark>();
        services.AddSingleton<CoreBenchmark>();
        services.AddSingleton<CrossPassBenchmark>();
        services.AddSingleton<DepthBenchmark>();
        services.AddSingleton<DepthDiagnosticBenchmark>();
        services.AddSingleton<DepthSkyBenchmark>();
        services.AddSingleton<FeatureBenchmark>();
        services.AddSingleton<FeatureOverlay>();
        services.AddSingleton<FusionIntegrationBenchmark>();
        services.AddSingleton<FusionPlanningBenchmark>();
        services.AddSingleton<FusionResumeBenchmark>();
        services.AddSingleton<MeshIndexBenchmark>();
        services.AddSingleton<MetalDepthBenchmark>();
        services.AddSingleton<PairSelectionBenchmark>();
        services.AddSingleton<PairVerificationBenchmark>();
        services.AddSingleton<SceneStorageBenchmark>();
        services.AddSingleton<SimplificationBenchmark>();
    }
}
