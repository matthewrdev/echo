using Echo.Infrastructure;
using Echo.Texturing.Charts;
using Echo.Texturing.Layout;
using Echo.Texturing.Rasterization;
using Echo.Texturing.Seams;
using Echo.Texturing.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Texturing;

public static class TexturingServiceCollectionExtensions
{
    public static IServiceCollection AddTexturing(this IServiceCollection services)
    {
        services.AddSingleton<ITextureBaker, TextureBaker>();
        services.AddSingleton<ITextureChartOrienter, TextureChartOrienter>();
        services.AddSingleton<ITextureGapPacker, TextureGapPacker>();
        services.AddSingleton<IJsonReadMetadata, TextureJsonReadMetadata>();
        services.AddSingleton<ITextureLayoutPlanner, TextureLayoutPlanner>();
        services.AddSingleton<ITexturePatchBuilder, TexturePatchBuilder>();
        services.AddSingleton<ITexturePatchPacker, TexturePatchPacker>();
        services.AddSingleton<ITextureProfileBuilder, TextureProfileBuilder>();
        services.AddSingleton<ITextureProfilePacker, TextureProfilePacker>();
        services.AddSingleton<ITextureRasterizer, TextureRasterizer>();
        services.AddSingleton<ITextureSeamLeveler, TextureSeamLeveler>();

        return services;
    }
}
