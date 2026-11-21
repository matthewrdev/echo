using Echo.Benchmarks;
using Echo.Composition;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
new ProcessingServices().Register(services);
new BenchmarkServices().Register(services);
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

if (args.FirstOrDefault() == "simplification")
{
    return provider.GetRequiredService<SimplificationBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "simplification-artifact")
{
    return provider.GetRequiredService<SimplificationBenchmark>().RunArtifact(args[1..]);
}
if (args.FirstOrDefault() == "fusion-planning")
{
    return provider.GetRequiredService<FusionPlanningBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "mesh-index")
{
    return provider.GetRequiredService<MeshIndexBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "fusion-resume")
{
    return provider.GetRequiredService<FusionResumeBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "fusion-integration")
{
    return provider.GetRequiredService<FusionIntegrationBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "scene-storage")
{
    return provider.GetRequiredService<SceneStorageBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "depth-diagnostic")
{
    return provider.GetRequiredService<DepthDiagnosticBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "depth-sky")
{
    return provider.GetRequiredService<DepthSkyBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "metal-depth")
{
    return provider.GetRequiredService<MetalDepthBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "align")
{
    return provider.GetRequiredService<AlignmentBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "verify-pair")
{
    return provider.GetRequiredService<PairVerificationBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "cross-pass")
{
    return provider.GetRequiredService<CrossPassBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "pairs")
{
    return provider.GetRequiredService<PairSelectionBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "bundle")
{
    return provider.GetRequiredService<BundleBenchmark>().Run();
}
if (args.FirstOrDefault() == "features")
{
    return provider.GetRequiredService<FeatureBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "cloud-publication")
{
    return provider.GetRequiredService<CloudPublicationBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "cloud")
{
    return provider.GetRequiredService<CloudBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "compare-cloud")
{
    return provider.GetRequiredService<CloudComparison>().Run(args[1..]);
}
if (args.FirstOrDefault() == "feature-overlay")
{
    return provider.GetRequiredService<FeatureOverlay>().Run(args[1..]);
}
if (args.FirstOrDefault() == "core")
{
    return provider.GetRequiredService<CoreBenchmark>().Run(args[1..]);
}
return provider.GetRequiredService<DepthBenchmark>().Run(args);
