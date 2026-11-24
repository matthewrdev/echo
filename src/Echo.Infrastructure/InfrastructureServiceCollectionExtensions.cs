using Microsoft.Extensions.DependencyInjection;

namespace Echo.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IJsonReadMetadata, CoreJsonReadMetadata>();
        services.AddSingleton<IFileOperations, FileOperations>();
        services.AddSingleton<IJsonCodec, JsonCodec>();

        return services;
    }
}
