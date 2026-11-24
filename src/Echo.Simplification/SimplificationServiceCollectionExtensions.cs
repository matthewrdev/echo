using Microsoft.Extensions.DependencyInjection;

namespace Echo.Simplification;

public static class SimplificationServiceCollectionExtensions
{
    public static IServiceCollection AddSimplification(this IServiceCollection services)
    {
        services.AddSingleton<IEdgeCollapse, EdgeCollapse>();

        return services;
    }
}
