using Echo.Contracts;
using Echo.Storage;

namespace Echo.Pipeline;

// Each replacement owns its output buffers. A no-op filter may return its input;
// retain that input's scope and discard only the unused replacement scope.
internal sealed class MeshGeometryBuffers : IDisposable
{
    private readonly WorkBuffers? parent;
    private WorkBuffers? current;
    private Surface? surface;

    public MeshGeometryBuffers(WorkBuffers? parent)
    {
        this.parent = parent;
    }

    public Surface Replace(Func<WorkBuffers?, Surface> create)
    {
        var next = parent?.CreateScope();
        try
        {
            var result = create(next);
            if (ReferenceEquals(result, surface))
            {
                next?.Dispose();
            }
            else
            {
                current?.Dispose();
                current = next;
                surface = result;
            }
            return result;
        }
        catch
        {
            next?.Dispose();
            throw;
        }
    }

    public void Dispose() => current?.Dispose();
}
