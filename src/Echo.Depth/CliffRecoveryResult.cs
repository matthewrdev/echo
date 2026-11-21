using Echo.Models;

namespace Echo.Depth;

public sealed class CliffRecoveryResult
{
    public required DepthMap Map
    {
        get; init;
    }
    public int SearchedSamples
    {
        get; init;
    }
    public int RecoveredSamples
    {
        get; init;
    }
    public int SourceImageWidth
    {
        get; init;
    }
    public int Neighbours
    {
        get; init;
    }
}
