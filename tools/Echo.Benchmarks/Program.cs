using Echo.Benchmarks;
using Echo.Composition;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(DepthBenchmark).Assembly, typeof(Processor).Assembly);
if (args.FirstOrDefault() == "features")
{
    return composition.Resolve<FeatureBenchmark>().Run(args[1..]);
}
return composition.Resolve<DepthBenchmark>().Run(args);
