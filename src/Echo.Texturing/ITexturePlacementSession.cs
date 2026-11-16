using Echo.Contracts;

namespace Echo.Texturing;

public interface ITexturePlacementSession : IDisposable
{
    ComputeBackend Backend
    {
        get;
    }
    string Device
    {
        get;
    }
    string Implementation
    {
        get;
    }
    TextureMaskPlacement? Find(TextureAtlasOccupancy atlas, IReadOnlyList<TextureMask> masks,
        int maximumBottom, CancellationToken cancellation, int minimumBottom = 1);
}
