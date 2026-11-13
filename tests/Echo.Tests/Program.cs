using Echo.Cli;
using Echo.Composition;
using Echo.Tests;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(ReconstructionTests).Assembly, typeof(Processor).Assembly, typeof(CommandApplication).Assembly);
var scaleTests = new TestSuite();
composition.Resolve<ScaleSpaceFeatureTests>().Run(scaleTests);
composition.Resolve<GuidedMatchingTests>().Run(scaleTests);
composition.Resolve<AlignmentVerificationTests>().Run(scaleTests);
composition.Resolve<Matrix3InverterTests>().Run(scaleTests);
composition.Resolve<RunRecoveryTests>().Run(scaleTests);
composition.Resolve<DepthProgressTests>().Run(scaleTests);
composition.Resolve<ArtifactStorageTests>().Run(scaleTests);
return Math.Max(scaleTests.Finish(), composition.Resolve<ReconstructionTests>().Run());
