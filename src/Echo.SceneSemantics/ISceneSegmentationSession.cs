using Echo.Contracts;

namespace Echo.SceneSemantics;

public interface ISceneSegmentationSession : IDisposable
{
    SemanticMap Segment(string imagePath, string imageId, CancellationToken cancellation);
}
