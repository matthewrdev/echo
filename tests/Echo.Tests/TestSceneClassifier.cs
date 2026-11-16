using Echo.Contracts;
using Echo.SceneSemantics;

namespace Echo.Tests;

public sealed class TestSceneClassifier : ISemanticModelProvider, ISceneSegmenter, ISceneSegmentationSession
{
    public string Fingerprint => "test-model-fingerprint";
    public int ModelRequests
    {
        get; private set;
    }
    public int Classifications
    {
        get; private set;
    }
    public bool AllowProcessing { get; set; } = true;

    public string EnsureAvailable(string directory, CancellationToken cancellation, Action<string>? progress = null)
    {
        ModelRequests++;
        if (!AllowProcessing)
        {
            throw new InvalidOperationException("Cached classifications must not load or download a model");
        }

        return "test-model";
    }

    public ISceneSegmentationSession Open(string modelPath) => this;

    public SemanticMap Segment(string imagePath, string imageId, CancellationToken cancellation)
    {
        Classifications++;
        return new SemanticMap { ImageId = imageId, Width = 1, Height = 1, Sky = [255], Vegetation = [0], Rock = [0] };
    }

    public void Dispose()
    {
    }
}
