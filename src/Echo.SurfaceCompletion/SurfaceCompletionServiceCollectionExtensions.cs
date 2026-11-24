using Microsoft.Extensions.DependencyInjection;

namespace Echo.SurfaceCompletion;

public static class SurfaceCompletionServiceCollectionExtensions
{
    public static IServiceCollection AddSurfaceCompletion(this IServiceCollection services)
    {
        services.AddSingleton<IPoissonSurfaceCodec, PoissonSurfaceCodec>();
        services.AddSingleton<ISurfaceReconstructor, PoissonSurfaceReconstructor>();

        return services;
    }
}
