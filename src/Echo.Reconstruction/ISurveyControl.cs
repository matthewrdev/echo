using Echo.Models;

namespace Echo.Reconstruction;

public interface ISurveyControl
{
    SurveyControlReport Measure(ProjectData project, ReconstructionScene scene, IReadOnlySet<string> controlIds,
        CancellationToken cancellation);

    SurveyControlReport Apply(ProjectData project, ReconstructionScene scene, CancellationToken cancellation);
}
