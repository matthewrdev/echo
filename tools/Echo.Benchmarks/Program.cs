using Echo.Benchmarks;
using Echo.Composition;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(DepthBenchmark).Assembly, typeof(Processor).Assembly);
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
if (args.FirstOrDefault() == "core")
{
    return composition.Resolve<CoreBenchmark>().Run(args[1..]);
}
return composition.Resolve<DepthBenchmark>().Run(args);
