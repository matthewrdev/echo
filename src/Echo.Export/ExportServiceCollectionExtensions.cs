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
        services.AddSingleton<IMappingTools, MappingTools>();
        services.AddSingleton<IMappingMosaicWriter, MappingMosaicWriter>();
        services.AddSingleton<IMappingProductWriter, MappingProductWriter>();
        services.AddSingleton<IMeshExchangeWriter, MeshExchangeWriter>();
        services.AddSingleton<IPointCloudExchangeWriter, PointCloudExchangeWriter>();
        services.AddSingleton<IPrintMeshWriter, ThreeMfWriter>();
        services.AddSingleton<ISurveyCloudWriter, PdalSurveyCloudWriter>();
        services.AddSingleton<IRedpointGeoreferenceWriter, RedpointGeoreferenceWriter>();
        services.AddSingleton<ITilesetWriter, TilesetWriter>();

        return services;
    }
}
