namespace Echo.Texturing;

public interface ITexturePatchPacker
{
    double Pack(IReadOnlyList<TexturePatch> patches, int size, int gutter, CancellationToken cancellation = default);
}
