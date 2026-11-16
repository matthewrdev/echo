namespace Echo.Texturing;

public interface ITexturePlacementAccelerator
{
    ITexturePlacementSession Open(int size, CancellationToken cancellation);
}
