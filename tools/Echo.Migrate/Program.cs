using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Metadata;
using Echo.Migrate;
using Echo.Projects;
using Echo.Storage;
using Echo.Telemetry;
using Microsoft.Extensions.DependencyInjection;

var alignmentOnly = args.FirstOrDefault() == "--alignment-previews-only";
var projects = alignmentOnly ? args.Skip(1).ToArray() : args;
if (projects.Length == 0)
{
    Console.Error.WriteLine("Usage: Echo.Migrate [--alignment-previews-only] PROJECT [PROJECT ...]");
    return 2;
}
var services = new ServiceCollection();
services
    .AddImaging()
    .AddInfrastructure()
    .AddMetadata()
    .AddProjects()
    .AddStorage()
    .AddTelemetry()
    .AddMigrate();

using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<MatchingProjectMigrator>();
var exitCode = 0;
foreach (var path in projects)
{
    try
    {
        var root = Path.GetFullPath(path);
        if (alignmentOnly)
        {
            migrator.MigrateAlignmentPreviews(root);
        }
        else
        {
            migrator.Migrate(root);
        }
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"{path}: {error.Message}");
        exitCode = 1;
    }
}

return exitCode;
