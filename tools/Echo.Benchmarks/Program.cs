using Echo.Benchmarks;
using Echo.Composition;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(DepthBenchmark).Assembly, typeof(Processor).Assembly);
if (args.FirstOrDefault() == "metal-depth")
{
    return composition.Resolve<MetalDepthBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "align")
{
    return composition.Resolve<AlignmentBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "verify-pair")
{
    return composition.Resolve<PairVerificationBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "cross-pass")
{
    return composition.Resolve<CrossPassBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "pairs")
{
    return composition.Resolve<PairSelectionBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "bundle")
{
    return composition.Resolve<BundleBenchmark>().Run();
}
if (args.FirstOrDefault() == "features")
{
    return composition.Resolve<FeatureBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "cloud")
{
    return composition.Resolve<CloudBenchmark>().Run(args[1..]);
}
if (args.FirstOrDefault() == "compare-cloud")
{
    return composition.Resolve<CloudComparison>().Run(args[1..]);
}
if (args.FirstOrDefault() == "feature-overlay")
{
    return composition.Resolve<FeatureOverlay>().Run(args[1..]);
}
if (args.FirstOrDefault() == "core")
{
    return composition.Resolve<CoreBenchmark>().Run(args[1..]);
}
return composition.Resolve<DepthBenchmark>().Run(args);
