using System.Buffers;
using System.Numerics;
using Echo.Models;
using Echo.Storage;

namespace Echo.Meshing;

internal sealed class SurfaceIndexBuffers : IDisposable
{
    private readonly WorkBuffers? scratch;
    private readonly PageMemoryReservation? indexReservation;
    private PageMemoryReservation? centreReservation;
    private WorkBuffers? centreScratch;
    private double[]? centres;

    internal WorkBuffers? Scratch => scratch;

    public IList<int> Order { get; private set; } = [];
    public IList<SurfaceSearchNode> Nodes { get; private set; } = [];

    public SurfaceIndexBuffers(Surface mesh, WorkBuffers? work, CancellationToken cancellation)
    {
        scratch = work?.CreateScope()
            ?? (mesh.Faces as PagedList<int[]>)?.CreateScratchBuffers()
            ?? (mesh.Vertices as PagedList<double[]>)?.CreateScratchBuffers();
        try
        {
            var count = mesh.Faces.Count;
            var leaves = BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, (count + 11L) / 12));
            var capacity = checked((int)(2L * leaves - 1));
            var bytes = checked(count * 4L + capacity * 64L + 256);
            indexReservation = scratch?.TryReserveMemory(bytes);
            if (scratch == null || indexReservation != null)
            {
                Order = new int[count];
                Nodes = new List<SurfaceSearchNode>(capacity);
            }
            else
            {
                Order = scratch.Filled(count, 0, new IntRecordCodec(), cancellation);
                Nodes = scratch.List(new SurfaceSearchNodeCodec());
            }
            for (var index = 0; index < count; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                }
                Order[index] = index;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IList<double> RentCentres(int count, CancellationToken cancellation)
    {
        var capacity = Math.Max(16L, BitOperations.RoundUpToPowerOf2((uint)count));
        centreReservation = scratch?.TryReserveMemory(checked(capacity * sizeof(double) + 128));
        if (scratch == null || centreReservation != null)
        {
            return centres = ArrayPool<double>.Shared.Rent(count);
        }
        centreScratch = scratch.CreateScope(preferResident: true);
        return centreScratch.Filled(count, 0.0, new DoubleRecordCodec(), cancellation);
    }

    public void ReleaseCentres()
    {
        if (centres != null)
        {
            ArrayPool<double>.Shared.Return(centres);
            centres = null;
        }
        centreReservation?.Dispose();
        centreReservation = null;
        centreScratch?.Dispose();
        centreScratch = null;
    }

    public void Dispose()
    {
        ReleaseCentres();
        Order = [];
        Nodes = [];
        indexReservation?.Dispose();
        scratch?.Dispose();
    }
}
