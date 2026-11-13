using Echo.Composition;
using Echo.Tests;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(ReconstructionTests).Assembly, typeof(Processor).Assembly);
return composition.Resolve<ReconstructionTests>().Run();
