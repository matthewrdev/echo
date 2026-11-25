using Echo.Meshing.Geometry;
using Echo.Models;

namespace Echo.Meshing;

internal sealed class MeshRaycasterFactory : IMeshRaycasterFactory
{
    public IMeshRaycaster Create(Surface surface, CancellationToken cancellation) =>
        new SurfaceTriangleIndex(surface, cancellation);
}
