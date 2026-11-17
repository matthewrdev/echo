using System.Buffers;
using Echo.Contracts;

namespace Echo.Fusion;

internal sealed class FusionChunkIntegrator : IDisposable
{
    private readonly Func<FusionBatchIntegrator> prepare;
    private FusionBatchIntegrator? integrator;
    private readonly FusionGridReader reader;
    private VolumeSample[]? output;
    private readonly CancellationToken cancellation;

    public FusionChunkIntegrator(Func<FusionBatchIntegrator> prepare, FusionGridReader reader, int capacity, CancellationToken cancellation)
    {
        this.prepare = prepare;
        this.reader = reader;
        this.cancellation = cancellation;
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity
    {
        get;
    }

    public ArraySegment<VolumeSample> Integrate(int count, Action<int>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        integrator ??= prepare();
        output ??= ArrayPool<VolumeSample>.Shared.Rent(Capacity);
        var retained = 0;
        for (var first = 0; first < count; first += integrator.Capacity)
        {
            cancellation.ThrowIfCancellationRequested();
            var length = Math.Min(integrator.Capacity, count - first);
            if (reader.Read(integrator.Cells.AsSpan(0, length)) != length)
            {
                throw new InvalidDataException("Fusion grid ranges ended before their declared cell count.");
            }
            var samples = integrator.Integrate(length);
            samples.AsSpan().CopyTo(output.AsSpan(retained));
            retained += samples.Count;
            progress?.Invoke(first + length);
        }
        cancellation.ThrowIfCancellationRequested();
        return new ArraySegment<VolumeSample>(output, 0, retained);
    }

    public void Skip(int count) => reader.Skip(count);

    public void Dispose()
    {
        integrator?.Dispose();
        if (output != null)
        {
            ArrayPool<VolumeSample>.Shared.Return(output);
            output = null;
        }
    }
}
