using Microsoft.Extensions.DependencyInjection;

namespace Echo.Migrate;

public sealed class MigrationServices
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<MatchingArtifactMigration>();
        services.AddSingleton<MatchingProjectMigrator>();
    }
}
