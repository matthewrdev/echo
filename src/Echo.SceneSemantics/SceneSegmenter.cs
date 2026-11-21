using Echo.Imaging;
using Echo.Telemetry;
using Echo.Models;
using Microsoft.ML.OnnxRuntime;

namespace Echo.SceneSemantics;

public sealed class SceneSegmenter : ISceneSegmenter
{
    private readonly IRasterImageService images;
    private readonly IPerformanceTrace trace;

    public SceneSegmenter(IRasterImageService images, IPerformanceTrace trace)
    {
        this.images = images;
        this.trace = trace;
    }

    public string GetImplementation(ComputeBackend backend)
    {
        if (backend == ComputeBackend.Cpu || !OperatingSystem.IsMacOS())
        {
            return "onnx-cpu-1";
        }

        return OrtEnv.Instance().GetAvailableProviders().Contains("CoreMLExecutionProvider", StringComparer.Ordinal)
            ? "onnx-coreml-mlprogram-all-static-1" : "onnx-cpu-1";
    }

    public ISceneSegmentationSession Open(string modelPath, ComputeBackend backend = ComputeBackend.Auto)
        => new SceneSegmentationSession(modelPath, images, trace, GetImplementation(backend) != "onnx-cpu-1");
}
