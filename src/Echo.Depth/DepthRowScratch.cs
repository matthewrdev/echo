using System.Buffers;

namespace Echo.Depth;

internal sealed class DepthRowScratch : IDisposable
{
    private readonly double[] values;
    private readonly int hypothesisCount;
    private readonly int costCount;

    public DepthRowScratch(int hypothesisCount, int costCount, int batchCapacity = 0)
    {
        this.hypothesisCount = hypothesisCount;
        this.costCount = costCount;
        values = ArrayPool<double>.Shared.Rent(hypothesisCount + costCount);
        try
        {
            Batch = batchCapacity == 0 ? null : new DepthBatchScratch(batchCapacity, hypothesisCount, costCount);
        }
        catch
        {
            ArrayPool<double>.Shared.Return(values);
            throw;
        }
    }

    public Span<double> Hypotheses => values.AsSpan(0, hypothesisCount);
    public Span<double> Costs => values.AsSpan(hypothesisCount, costCount);
    public DepthBatchScratch? Batch
    {
        get;
    }

    public int GlobalSearches
    {
        get; set;
    }
    public int RecoveredSamples
    {
        get; set;
    }
    public long AcceleratedSearches
    {
        get; set;
    }
    public long CpuSearches
    {
        get; set;
    }
    public long ValidatedSearches
    {
        get; set;
    }
    public long MatchedSearches
    {
        get; set;
    }

    public void Dispose()
    {
        Batch?.Dispose();
        ArrayPool<double>.Shared.Return(values);
    }
}
