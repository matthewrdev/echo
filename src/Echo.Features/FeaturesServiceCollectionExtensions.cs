using Microsoft.Extensions.DependencyInjection;

namespace Echo.Features;

public static class FeaturesServiceCollectionExtensions
{
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        services.AddSingleton<IFeatureExtractor, FeatureExtractor>();

        return services;
    }
}
