namespace Echo.Texturing;

public interface ITexturePlacementAccelerator
{
    string Implementation
    {
        get;
    }
    ITexturePlacementSession Open(int size, CancellationToken cancellation);
}
