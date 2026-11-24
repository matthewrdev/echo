using Echo.Texturing.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Texturing.Metal;

public static class MetalTexturingServiceCollectionExtensions
{
    public static IServiceCollection AddMetalTexturing(this IServiceCollection services)
    {
        services.AddSingleton<ITexturePlacementAccelerator, MetalTexturePlacementAccelerator>();

        return services;
    }
}
