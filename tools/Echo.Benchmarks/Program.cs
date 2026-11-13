using Echo.Benchmarks;
using Echo.Composition;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(DepthBenchmark).Assembly, typeof(Processor).Assembly);
return composition.Resolve<DepthBenchmark>().Run(args);
