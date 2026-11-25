using Microsoft.Extensions.DependencyInjection;

namespace Echo.ImageProjection;

public static class ImageProjectionServiceCollectionExtensions
{
    public static IServiceCollection AddImageProjection(this IServiceCollection services)
    {
        services.AddSingleton<IImageGeometryProjection, ImageGeometryProjection>();
        services.AddSingleton<IExternalImageRegistration, ExternalImageRegistration>();

        return services;
    }
}
