using Echo.Export;
using Echo.Projects;

namespace Echo.Pipeline.Exporting;

public interface IRunExchangeService
{
    RunExchangeResult ExportMesh(IProjectStore store, string runId, string destination, MeshExportFormat format, double? sizeMillimetres = null);
    RunExchangeResult ExportImages(IProjectStore store, string runId, string destination);
    RunExchangeResult ExportPointCloud(IProjectStore store, string runId, string destination, PointCloudExportFormat format, string? crs = null);
}
