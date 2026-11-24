using Microsoft.Extensions.DependencyInjection;

namespace Echo.DenseCloud;

public static class DenseCloudServiceCollectionExtensions
{
    public static IServiceCollection AddDenseCloud(this IServiceCollection services)
    {
        services.AddSingleton<ICloudBuilder, CloudBuilder>();
        services.AddSingleton<ICloudExecutionPlanner, CloudExecutionPlanner>();

        return services;
    }
}
