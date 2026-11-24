using Microsoft.Extensions.DependencyInjection;

namespace Echo.Optimization;

public static class OptimizationServiceCollectionExtensions
{
    public static IServiceCollection AddOptimization(this IServiceCollection services)
    {
        services.AddSingleton<IBundleAdjuster, BundleAdjuster>();

        return services;
    }
}
