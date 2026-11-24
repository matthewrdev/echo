using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Metadata;
using Echo.Projects;
using Echo.Storage;
using Echo.Migrate;
using Microsoft.Extensions.DependencyInjection;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Echo.Migrate PROJECT [PROJECT ...]");
    return 2;
}
var services = new ServiceCollection();
services
    .AddImaging()
    .AddInfrastructure()
    .AddMetadata()
    .AddProjects()
    .AddStorage()
    .AddMigrate();
using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<MatchingProjectMigrator>();
foreach (var path in args)
{
    migrator.Migrate(Path.GetFullPath(path));
}
return 0;
