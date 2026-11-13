using Echo.Cli;
using Echo.Composition;
using Echo.Tests;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(ReconstructionTests).Assembly, typeof(Processor).Assembly, typeof(CommandApplication).Assembly);
return composition.Resolve<ReconstructionTests>().Run();
