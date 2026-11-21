using Echo.Compute;
using Echo.Models;
using Echo.Geometry;

namespace Echo.Meshing.Volumes;

// At most one GPU request runs while the caller triangulates the previous
// result. Session access stays serialized and disposal drains the prefetch.
internal sealed class VolumeCellReader : IDisposable
{
    private readonly IEnumerator<Grid> keys;
    private readonly IComputeSession? session;
    private readonly ComputeBackend backend;
    private readonly CancellationToken cancellation;
    private readonly int mask;
    private VolumeCellBatch current;
    private VolumeCellBatch next;
    private Task<int>? pending;
    private bool failed;

    public VolumeCellReader(IEnumerable<Grid> keys, IComputeSession? session, ComputeBackend backend, int mask, CancellationToken cancellation)
    {
        this.keys = keys.GetEnumerator();
        this.session = session;
        this.backend = backend;
        this.mask = mask;
        this.cancellation = cancellation;
        current = new VolumeCellBatch(session != null);
        next = new VolumeCellBatch(session != null);
    }

    public VolumeCellBatch? Read()
    {
        cancellation.ThrowIfCancellationRequested();
        if (next.Count == 0)
        {
            Prefetch();
        }
        if (next.Count == 0)
        {
            return null;
        }
        next.Accelerated = pending != null;
        next.ActiveCount = next.Count;
        if (pending != null)
        {
            var operation = pending;
            pending = null;
            try
            {
                next.ActiveCount = operation.GetAwaiter().GetResult();
            }
            catch (ComputeException) when (backend == ComputeBackend.Auto)
            {
                failed = true;
                next.Accelerated = false;
            }
        }
        var previous = current;
        current = next;
        next = previous;
        next.Count = 0;
        Prefetch();
        return current;
    }

    private void Prefetch()
    {
        cancellation.ThrowIfCancellationRequested();
        while (next.Count < next.Keys.Length && keys.MoveNext())
        {
            next.Keys[next.Count++] = keys.Current;
        }
        if (session != null && !failed && next.Count > 0)
        {
            var batch = next;
            pending = Task.Run(() => SparseVolumeCompute.ReadActive(session, batch.Keys.AsSpan(0, batch.Count), batch.Records, mask));
        }
    }

    public void Dispose()
    {
        try
        {
            // An unused prefetched result must release the session before its
            // owner disposes it, including cancellation or a caller exception.
            pending?.GetAwaiter().GetResult();
        }
        catch (Exception) when (pending != null)
        {
            // Read propagates consumed failures. An unconsumed result must not
            // replace the exception which caused the caller to stop early.
        }
        keys.Dispose();
    }
}
