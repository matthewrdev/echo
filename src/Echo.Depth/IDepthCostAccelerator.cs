namespace Echo.Depth;

public interface IDepthCostAccelerator
{
    string Backend
    {
        get;
    }
    IDepthCostSession Open(StereoView[] views, CancellationToken cancellation);
}
