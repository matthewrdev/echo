using Echo.Texturing;

namespace Echo.Tests;

internal sealed class UnavailableTexturePlacementAccelerator : ITexturePlacementAccelerator
{
    public int OpenCount
    {
        get; private set;
    }

    public ITexturePlacementSession Open(int size, CancellationToken cancellation)
    {
        OpenCount++;
        throw new TextureAccelerationException("Unavailable test device");
    }
}
