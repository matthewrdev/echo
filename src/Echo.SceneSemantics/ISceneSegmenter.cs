using Echo.Models;

namespace Echo.SceneSemantics;

public interface ISceneSegmenter
{
    string GetImplementation(ComputeBackend backend);
    ISceneSegmentationSession Open(string modelPath, ComputeBackend backend = ComputeBackend.Auto);
}
