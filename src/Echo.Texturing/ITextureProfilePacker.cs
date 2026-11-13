namespace Echo.Texturing;

public interface ITextureProfilePacker
{
    bool TryPack(IReadOnlyList<TexturePatch> patches, int size, int gutter, double scale, CancellationToken cancellation);
}
