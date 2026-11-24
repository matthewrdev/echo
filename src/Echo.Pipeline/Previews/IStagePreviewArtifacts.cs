using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Previews;

public interface IStagePreviewArtifacts
{
    FeatureSet Features(IProjectStore store, RunData run, string imageId);
    ImagePair Matching(IProjectStore store, RunData run, string firstId, string secondId);
    ReconstructionScene Alignment(IProjectStore store, RunData run);
    string DepthPath(IProjectStore store, RunData run, string imageId);
    DenseCloudReport Cloud(IProjectStore store, RunData run, bool includePoints = true);
    StageCameraInputs CameraInputs(IProjectStore store, RunData run, string stage);
    IReadOnlyList<CameraPose> Poses(IProjectStore store, RunData run);
    string FusionPath(IProjectStore store, RunData run);
    void WriteTextureModel(IProjectStore store, RunData run, string output);
    string ExportPath(IProjectStore store, RunData run);
    string MeshPath(IProjectStore store, RunData run);
}
