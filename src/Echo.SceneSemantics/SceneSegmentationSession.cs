using Echo.Models;
using Echo.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Echo.Telemetry;

namespace Echo.SceneSemantics;

public sealed class SceneSegmentationSession : ISceneSegmentationSession
{
    private readonly InferenceSession session;
    private readonly IRasterImageService images;
    private readonly IPerformanceTrace? trace;
    private readonly PerformanceBackend inferenceBackend;

    public SceneSegmentationSession(string modelPath, IRasterImageService images, IPerformanceTrace? trace = null, bool useCoreML = false)
    {
        this.images = images;
        this.trace = trace;
        inferenceBackend = useCoreML ? PerformanceBackend.Mixed : PerformanceBackend.Cpu;
        using var options = new SessionOptions { IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount), InterOpNumThreads = 1 };
        if (useCoreML)
        {
            var cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath))!, "coreml-mlprogram-all-static-1");
            Directory.CreateDirectory(cache);
            options.AppendExecutionProvider("CoreML", new Dictionary<string, string>
            {
                ["ModelFormat"] = "MLProgram",
                ["MLComputeUnits"] = "ALL",
                ["RequireStaticInputShapes"] = "1",
                ["ModelCacheDirectory"] = cache
            });
        }

        session = new InferenceSession(modelPath, options);
    }

    public SemanticMap Segment(string imagePath, string imageId, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        trace?.Operation(new OperationProgress("Prepare semantic image", "Decoding and normalizing photograph"), PerformanceBackend.Cpu);
        using var bitmap = images.Decode(imagePath);
        using var resized = bitmap.Resize(new SKImageInfo(512, 512), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized == null)
        {
            throw new EchoException("Unable to resize the semantic input image.", 5);
        }

        var input = new DenseTensor<float>(new[] { 1, 3, 512, 512 });
        var pixels = resized.Pixels;
        for (var index = 0; index < pixels.Length; index++)
        {
            var colour = pixels[index];
            input.Buffer.Span[index] = (colour.Red / 255f - .485f) / .229f;
            input.Buffer.Span[pixels.Length + index] = (colour.Green / 255f - .456f) / .224f;
            input.Buffer.Span[2 * pixels.Length + index] = (colour.Blue / 255f - .406f) / .225f;
        }

        using var runOptions = new RunOptions();
        using var registration = cancellation.Register(() => runOptions.Terminate = true);
        trace?.Operation(new OperationProgress("Semantic inference", "Classifying sky, rock and vegetation"), inferenceBackend);
        using var output = Run(input, runOptions, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var logits = output.First().AsTensor<float>();
        var dimensions = logits.Dimensions;
        if (dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 150)
        {
            throw new EchoException("The scene model must produce ADE20K logits [1,150,height,width].", 5);
        }

        trace?.Operation(new OperationProgress("Decode semantic probabilities", "Combining semantic classes"), PerformanceBackend.Cpu);
        var values = logits is DenseTensor<float> dense ? dense.Buffer.Span : logits.ToArray();
        return SemanticProbabilities.Decode(imageId, dimensions[3], dimensions[2], values);
    }

    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(DenseTensor<float> input, RunOptions options, CancellationToken cancellation)
    {
        try
        {
            return session.Run([NamedOnnxValue.CreateFromTensor("pixel_values", input)], ["logits"], options);
        }
        catch (OnnxRuntimeException) when (cancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellation);
        }
    }

    public void Dispose() => session.Dispose();
}
