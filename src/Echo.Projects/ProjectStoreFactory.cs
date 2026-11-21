using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Metadata;

namespace Echo.Projects;

public sealed class ProjectStoreFactory : IProjectStoreFactory
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IMetadataReader metadataReader;
    private readonly IRasterImageService rasterImageService;

    public ProjectStoreFactory(IFileOperations fileOperations,
        IJsonCodec jsonCodec,
        IMetadataReader metadataReader,
        IRasterImageService rasterImageService)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.metadataReader = metadataReader;
        this.rasterImageService = rasterImageService;
    }

    public IProjectStore Open(string root)
    {
        return new ProjectStore(root, fileOperations, jsonCodec, metadataReader, rasterImageService);
    }
}
