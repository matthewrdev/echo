using Microsoft.Extensions.DependencyInjection;

namespace Echo.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddMetadata(this IServiceCollection services)
    {
        services.AddSingleton<MetadataReader>();
        services.AddSingleton<IMetadataReader>(provider => provider.GetRequiredService<MetadataReader>());
        services.AddSingleton<IMetadataPayloadReader>(provider => provider.GetRequiredService<MetadataReader>());

        return services;
    }
}
