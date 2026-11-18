using Echo.Composition;
using Echo.Migrate;
using Echo.Pipeline;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Echo.Migrate PROJECT [PROJECT ...]");
    return 2;
}
using var composition = new MefComposition(typeof(MatchingProjectMigrator).Assembly, typeof(Processor).Assembly);
var migrator = composition.Resolve<MatchingProjectMigrator>();
foreach (var path in args)
{
    migrator.Migrate(Path.GetFullPath(path));
}
return 0;
