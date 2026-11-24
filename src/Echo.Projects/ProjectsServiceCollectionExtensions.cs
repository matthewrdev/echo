using Microsoft.Extensions.DependencyInjection;

namespace Echo.Projects;

public static class ProjectsServiceCollectionExtensions
{
    public static IServiceCollection AddProjects(this IServiceCollection services)
    {
        services.AddSingleton<IPhotoDeduplicator, PhotoDeduplicator>();
        services.AddSingleton<IPhotoDeletionService, PhotoDeletionService>();
        services.AddSingleton<IProjectManagementService, ProjectManagementService>();
        services.AddSingleton<IProjectStoreFactory, ProjectStoreFactory>();

        return services;
    }
}
