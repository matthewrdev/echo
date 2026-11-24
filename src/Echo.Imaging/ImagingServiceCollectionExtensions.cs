using Microsoft.Extensions.DependencyInjection;

namespace Echo.Imaging;

public static class ImagingServiceCollectionExtensions
{
    public static IServiceCollection AddImaging(this IServiceCollection services)
    {
        services.AddSingleton<IRasterImageService, RasterImageService>();

        return services;
    }
}
