using Echo.Contracts;

namespace Echo.SceneSemantics;

public interface ISceneSegmentationSession : IDisposable
{
    // Independent image calls may run concurrently; disposal follows their completion.
    SemanticMap Segment(string imagePath, string imageId, CancellationToken cancellation);
}
