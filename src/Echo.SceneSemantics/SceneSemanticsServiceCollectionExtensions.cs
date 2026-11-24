using Microsoft.Extensions.DependencyInjection;

namespace Echo.SceneSemantics;

public static class SceneSemanticsServiceCollectionExtensions
{
    public static IServiceCollection AddSceneSemantics(this IServiceCollection services)
    {
        services.AddSingleton<IModelAssetCache, ModelAssetCache>();
        services.AddSingleton<ISceneSegmenter, SceneSegmenter>();
        services.AddSingleton<ISemanticModelProvider, SemanticModelProvider>();

        return services;
    }
}
