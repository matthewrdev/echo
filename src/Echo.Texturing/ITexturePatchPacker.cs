using Echo.Contracts;
namespace Echo.Texturing;

public interface ITexturePatchPacker
{
    double? TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default,
        Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto, Action<OperationProgress>? operationProgress = null);
    double Pack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default,
        Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto, Action<OperationProgress>? operationProgress = null);
}
