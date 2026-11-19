using Echo.Composition;
using Echo.Validation;

using var composition = new MefComposition(typeof(ValidationApplication).Assembly,
    typeof(Echo.Compute.Metal.MetalComputeDevice).Assembly,
    typeof(Echo.Depth.Metal.MetalDepthCostAccelerator).Assembly,
    typeof(Echo.Texturing.Metal.MetalTexturePlacementAccelerator).Assembly);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    return composition.Resolve<ValidationApplication>().Run(args, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Validation cancelled; saved work and reports are retained.");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
