using System.Buffers;

namespace Echo.Depth;

internal sealed class DepthRowScratch : IDisposable
{
    private readonly double[] values;
    private readonly int hypothesisCount;
    private readonly int costCount;

    public DepthRowScratch(int hypothesisCount, int costCount)
    {
        this.hypothesisCount = hypothesisCount;
        this.costCount = costCount;
        values = ArrayPool<double>.Shared.Rent(hypothesisCount + costCount);
    }

    public Span<double> Hypotheses => values.AsSpan(0, hypothesisCount);
    public Span<double> Costs => values.AsSpan(hypothesisCount, costCount);

    public int GlobalSearches
    {
        get; set;
    }
    public int RecoveredSamples
    {
        get; set;
    }

    public void Dispose()
    {
        ArrayPool<double>.Shared.Return(values);
    }
}
