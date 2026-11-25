using System.Globalization;
using Echo.Cli;
using Echo.Compute;
using Echo.Compute.Metal;
using Echo.DenseCloud;
using Echo.Depth;
using Echo.Depth.Metal;
using Echo.Export;
using Echo.Features;
using Echo.Fusion;
using Echo.Imaging;
using Echo.ImageProjection;
using Echo.Infrastructure;
using Echo.Lines;
using Echo.Matching;
using Echo.Meshing;
using Echo.Metadata;
using Echo.Optimization;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Reconstruction;
using Echo.Rendering;
using Echo.SceneSemantics;
using Echo.Simplification;
using Echo.Storage;
using Echo.SurfaceCompletion;
using Echo.Telemetry;
using Echo.Texturing;
using Echo.Texturing.Metal;
using Microsoft.Extensions.DependencyInjection;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
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
    .AddImageProjection()
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
    .AddRendering()
    .AddCli();

using var provider = services.BuildServiceProvider();

return provider.GetRequiredService<ICommandApplication>().Run(args);
