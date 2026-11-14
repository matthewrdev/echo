using System.Globalization;
using Echo.Cli;
using Echo.Composition;
using Echo.Pipeline;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
using var composition = new MefComposition(typeof(CommandApplication).Assembly, typeof(Processor).Assembly, typeof(Echo.Depth.Metal.MetalDepthCostAccelerator).Assembly);
return composition.Resolve<ICommandApplication>().Run(args);
