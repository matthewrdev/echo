using Microsoft.Extensions.DependencyInjection;

namespace Echo.Export;

public static class ExportServiceCollectionExtensions
{
    public static IServiceCollection AddExport(this IServiceCollection services)
    {
        services.AddSingleton<IGeoPackageWriter, GeoPackageWriter>();
        services.AddSingleton<IGisWriter, GisWriter>();
        services.AddSingleton<IGlbWriter, GlbWriter>();
        services.AddSingleton<ILasWriter, LasWriter>();
        services.AddSingleton<IRedpointGeoreferenceWriter, RedpointGeoreferenceWriter>();
        services.AddSingleton<ITilesetWriter, TilesetWriter>();

        return services;
    }
}
