using System.Buffers;
using Echo.Models;

namespace Echo.DenseCloud;

internal sealed class CloudRowBatch : IDisposable
{
    private bool disposed;

    public CloudRowBatch(int capacity)
    {
        Samples = ArrayPool<DenseSample>.Shared.Rent(capacity);
    }

    public DenseSample[] Samples
    {
        get;
    }
    public int Count
    {
        get; set;
    }
    public int InputSamples
    {
        get; set;
    }
    public int Rows
    {
        get; set;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            ArrayPool<DenseSample>.Shared.Return(Samples);
            disposed = true;
        }
    }
}
