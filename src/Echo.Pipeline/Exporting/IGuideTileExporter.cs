using Echo.Models;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Texturing;

namespace Echo.Pipeline.Exporting;

public interface IGuideTileExporter
{
    void Write(IProjectStore store, RunData run, string directory, TexturedRunMesh textured,
        TextureResult texture, BulkDataSession session, int depth, string? detailTile, int leafTextureSize, CancellationToken cancellation);
}
