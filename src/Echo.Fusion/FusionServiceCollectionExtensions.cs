using Microsoft.Extensions.DependencyInjection;

namespace Echo.Fusion;

public static class FusionServiceCollectionExtensions
{
    public static IServiceCollection AddFusion(this IServiceCollection services)
    {
        services.AddSingleton<IDepthFusion, DepthFusion>();
        services.AddSingleton<IFusionResourcePlanner, FusionResourcePlanner>();

        return services;
    }
}
