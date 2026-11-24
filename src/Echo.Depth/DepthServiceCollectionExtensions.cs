using Microsoft.Extensions.DependencyInjection;

namespace Echo.Depth;

public static class DepthServiceCollectionExtensions
{
    public static IServiceCollection AddDepth(this IServiceCollection services)
    {
        services.AddSingleton<ICliffDepthRecovery, CliffDepthRecovery>();
        services.AddSingleton<IDepthEstimator, DepthEstimator>();
        services.AddSingleton<IDepthExecutionPlanner, DepthExecutionPlanner>();

        return services;
    }
}
