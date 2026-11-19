using Echo.Contracts;
using Echo.Meshing;
using Echo.Projects;

namespace Echo.Validation;

public interface IReconstructionMeasurements
{
    Dictionary<QualityMetric, double> Read(IProjectStore store, RunData run, CancellationToken cancellation);
    SurfaceTopologyReport Topology(IProjectStore store, RunData run, CancellationToken cancellation);
}
