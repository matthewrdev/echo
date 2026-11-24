using Echo.Compute;
using Echo.Compute.Metal;
using Echo.DenseCloud;
using Echo.Depth;
using Echo.Depth.Metal;
using Echo.Export;
using Echo.Features;
using Echo.Fusion;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Lines;
using Echo.Matching;
using Echo.Meshing;
using Echo.Metadata;
using Echo.Optimization;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Reconstruction;
using Echo.SceneSemantics;
using Echo.Simplification;
using Echo.Storage;
using Echo.SurfaceCompletion;
using Echo.Telemetry;
using Echo.Texturing;
using Echo.Texturing.Metal;
using Echo.Validation;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
Echo.Logging.LoggingServiceCollectionExtensions.AddLogging(services);
services
    .AddCompute()
    .AddMetalCompute()
    .AddDenseCloud()
    .AddDepth()
    .AddMetalDepth()
    .AddExport()
    .AddFeatures()
    .AddFusion()
    .AddImaging()
    .AddInfrastructure()
    .AddLines()
    .AddMatching()
    .AddMeshing()
    .AddMetadata()
    .AddOptimization()
    .AddPipeline()
    .AddProjects()
    .AddReconstruction()
    .AddSceneSemantics()
    .AddSimplification()
    .AddStorage()
    .AddSurfaceCompletion()
    .AddTelemetry()
    .AddTexturing()
    .AddMetalTexturing()
    .AddValidation();

using var provider = services.BuildServiceProvider();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    return provider.GetRequiredService<ValidationApplication>().Run(args, cancellation.Token);
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
