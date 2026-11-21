using Echo.Models;
namespace Echo.Texturing.Layout;

public interface ITextureGapPacker
{
    double Improve(IReadOnlyList<TexturePatch> patches, int size, int gutter, double baseline,
        CancellationToken cancellation, Action<string>? progress = null, ComputeBackend backend = ComputeBackend.Auto,
        long maximumCandidates = 64L * 1024 * 1024);
}
