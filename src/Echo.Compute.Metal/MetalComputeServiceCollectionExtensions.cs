using Echo.Compute;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Compute.Metal;

public static class MetalComputeServiceCollectionExtensions
{
    public static IServiceCollection AddMetalCompute(this IServiceCollection services)
    {
        services.AddSingleton<IComputeDevice, MetalComputeDevice>();

        return services;
    }
}
