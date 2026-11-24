using Microsoft.Extensions.DependencyInjection;

namespace Echo.Matching;

public static class MatchingServiceCollectionExtensions
{
    public static IServiceCollection AddMatching(this IServiceCollection services)
    {
        services.AddSingleton<IFeatureMatcher, FeatureMatcher>();
        services.AddSingleton<IGuidedFeatureMatcher, GuidedFeatureMatcher>();

        return services;
    }
}
