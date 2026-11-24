using Microsoft.Extensions.DependencyInjection;

namespace Echo.Migrate;

public static class MigrationServiceCollectionExtensions
{
    public static IServiceCollection AddMigrate(this IServiceCollection services)
    {
        services.AddSingleton<MatchingArtifactMigration>();
        services.AddSingleton<MatchingProjectMigrator>();

        return services;
    }
}
