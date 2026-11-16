using System.ComponentModel.Composition;

namespace Echo.SceneSemantics;

[Export(typeof(ISemanticModelProvider)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SemanticModelProvider : ISemanticModelProvider
{
    private readonly IModelAssetCache cache;
    private readonly ModelAsset model = new()
    {
        Name = "UPerNet sky, vegetation and rock model",
        FileName = "upernet-convnext-tiny-ade.onnx",
        Fingerprint = "fea729ab6c5d338bc79ad3d986b073e6faa12c97255eafd78a1f7145326e534e",
        DownloadUri = new Uri("https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx/resolve/a8e2807c3479bc617b37d8ed53e68660b3fa1ebb/model.onnx"),
        DownloadSize = "228 MB",
        MaximumBytes = 300L * 1024 * 1024
    };

    [ImportingConstructor]
    public SemanticModelProvider(IModelAssetCache cache)
    {
        this.cache = cache;
    }

    public string Fingerprint => model.Fingerprint;

    public string EnsureAvailable(string directory, CancellationToken cancellation, Action<string>? progress = null)
    {
        return cache.EnsureAvailable(model, directory, cancellation, progress);
    }
}
