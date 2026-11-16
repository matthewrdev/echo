using Echo.Contracts;
using Echo.Storage;
using Echo.Projects;

namespace Echo.DenseCloud;

public interface ICloudBuilder
{
    DenseCloudData Build(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        CancellationToken cancellation,
        Action<DenseCloudProgress>? progress = null,
        Action<DenseCloudReport>? preview = null, IList<DenseSample>? samples = null);
    DenseCloudReport Report(DenseCloudData cloud);
    void Write(string path, DenseCloudData cloud);
    DenseCloudData Read(string path, DenseCloudReport report, BulkDataSession? session = null);
}
