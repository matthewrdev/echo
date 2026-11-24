using Echo.Rendering.Previews;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Rendering;

public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddRendering(this IServiceCollection services)
    {
        services.AddSingleton<IModelRenderer, NativeModelRenderer>();
        services.AddSingleton<IPreviewPngWriter, PreviewPngWriter>();

        return services;
    }
}
