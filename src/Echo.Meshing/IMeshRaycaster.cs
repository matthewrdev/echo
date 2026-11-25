using Echo.Geometry;

namespace Echo.Meshing;

public interface IMeshRaycaster : IDisposable
{
    MeshRayHit? FirstHit(V3 origin, V3 direction, double minimumDepth = 1e-8);
}
