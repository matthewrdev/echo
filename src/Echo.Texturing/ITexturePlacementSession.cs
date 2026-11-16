namespace Echo.Texturing;

public interface ITexturePlacementSession : IDisposable
{
    string Backend
    {
        get;
    }
    TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation);
}
