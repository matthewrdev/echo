using Echo.Pipeline.Alignment;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Migrate;

public static class MigrationServiceCollectionExtensions
{
    public static IServiceCollection AddMigrate(this IServiceCollection services)
    {
        services.AddSingleton<MatchingArtifactMigration>();
        services.AddSingleton<MatchingProjectMigrator>();
        services.AddSingleton<PackedStorageMigrator>();
        services.AddSingleton<IAlignmentPreviewWriter, AlignmentPreviewWriter>();

        return services;
    }
}
