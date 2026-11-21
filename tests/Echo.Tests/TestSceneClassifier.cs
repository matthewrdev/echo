using Echo.Models;
using Echo.SceneSemantics;

namespace Echo.Tests;

public sealed class TestSceneClassifier : ISemanticModelProvider, ISceneSegmenter, ISceneSegmentationSession
{
    private int classifications;
    public string Fingerprint => "test-model-fingerprint";
    public int ModelRequests
    {
        get; private set;
    }
    public int Classifications => classifications;
    public bool AllowProcessing { get; set; } = true;
    public byte VegetationProbability
    {
        get; set;
    }

    public string EnsureAvailable(string directory, CancellationToken cancellation, Action<string>? progress = null)
    {
        ModelRequests++;
        if (!AllowProcessing)
        {
            throw new InvalidOperationException("Cached classifications must not load or download a model");
        }

        return "test-model";
    }

    public string Implementation { get; set; } = "test-cpu-1";

    public string GetImplementation(ComputeBackend backend) => Implementation;

    public ISceneSegmentationSession Open(string modelPath, ComputeBackend backend = ComputeBackend.Auto) => this;

    public SemanticMap Segment(string imagePath, string imageId, CancellationToken cancellation)
    {
        Interlocked.Increment(ref classifications);
        if (VegetationProbability > 0)
        {
            return new SemanticMap
            {
                ImageId = imageId,
                Width = 4,
                Height = 4,
                Vegetation = Enumerable.Repeat(VegetationProbability, 16).ToArray(),
                Rock = new byte[16],
                Sky = new byte[16]
            };
        }

        return new SemanticMap { ImageId = imageId, Width = 1, Height = 1, Sky = [255], Vegetation = [0], Rock = [0] };
    }

    public void Dispose()
    {
    }
}
