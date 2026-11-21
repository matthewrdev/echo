using Echo.Composition;
using Echo.Migrate;
using Microsoft.Extensions.DependencyInjection;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Echo.Migrate PROJECT [PROJECT ...]");
    return 2;
}
var services = new ServiceCollection();
new ProcessingServices().Register(services);
new MigrationServices().Register(services);
using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<MatchingProjectMigrator>();
foreach (var path in args)
{
    migrator.Migrate(Path.GetFullPath(path));
}
return 0;
