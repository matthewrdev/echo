using System.Buffers;
using Echo.Contracts;

namespace Echo.Fusion;

internal sealed class FusionChunkIntegrator : IDisposable
{
    private readonly FusionBatchIntegrator integrator;
    private readonly FusionGridReader reader;
    private readonly VolumeSample[] output;
    private readonly CancellationToken cancellation;

    public FusionChunkIntegrator(FusionBatchIntegrator integrator, FusionGridReader reader, int capacity, CancellationToken cancellation)
    {
        this.integrator = integrator;
        this.reader = reader;
        this.cancellation = cancellation;
        Capacity = Math.Max(integrator.Capacity, capacity);
        output = ArrayPool<VolumeSample>.Shared.Rent(Capacity);
    }

    public int Capacity
    {
        get;
    }

    public ArraySegment<VolumeSample> Integrate(int count, Action<int>? progress = null)
    {
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

    public void Dispose() => ArrayPool<VolumeSample>.Shared.Return(output);
}
