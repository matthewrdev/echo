using Echo.Depth;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Depth.Metal;

public static class MetalDepthServiceCollectionExtensions
{
    public static IServiceCollection AddMetalDepth(this IServiceCollection services)
    {
        services.AddSingleton<IDepthCostAccelerator, MetalDepthCostAccelerator>();

        return services;
    }
}
