using Echo.Storage.Artifacts;
using Echo.Storage.Matching;
using Echo.Storage.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.AddSingleton<IArtifactWriteObserver, ArtifactWriteObserver>();
        services.AddSingleton<IBinaryArtifactFiles, BinaryArtifactFiles>();
        services.AddSingleton<IBulkArtifactStore, BulkArtifactStore>();
        services.AddSingleton<IFeatureArtifactStore, FeatureArtifactStore>();
        services.AddSingleton<IMatchArtifactStore, MatchArtifactStore>();
        services.AddSingleton<IMatchingIndexStore, MatchingIndexStore>();
        services.AddSingleton<IProcessingResourcePlanner, ProcessingResourcePlanner>();

        return services;
    }
}
