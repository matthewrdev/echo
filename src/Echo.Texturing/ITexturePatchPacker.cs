namespace Echo.Texturing;

public interface ITexturePatchPacker
{
    double? TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default);
    double Pack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default);
}
