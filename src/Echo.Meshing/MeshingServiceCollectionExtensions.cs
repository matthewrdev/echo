using Microsoft.Extensions.DependencyInjection;

namespace Echo.Meshing;

public static class MeshingServiceCollectionExtensions
{
    public static IServiceCollection AddMeshing(this IServiceCollection services)
    {
        services.AddSingleton<IMeshBuilder, MeshBuilder>();
        services.AddSingleton<IMeshRaycasterFactory, MeshRaycasterFactory>();

        return services;
    }
}
