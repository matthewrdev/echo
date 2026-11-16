using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

// Decode compact geometry before entering parallel loops. Paged reads use
// value records so neither faces nor positions allocate small arrays.
internal sealed class SurfaceGeometryReader
{
    private readonly Surface surface;
    private readonly V3RecordCodec vertices = new();
    private readonly SurfaceTriangleVerticesCodec faces = new();

    public SurfaceGeometryReader(Surface surface)
    {
        this.surface = surface;
    }

    public V3 Vertex(int index)
    {
        if (surface.Vertices is PagedList<double[]> paged)
        {
            Span<V3> point = stackalloc V3[1];
            paged.ReadRange(index, point, vertices);
            return point[0];
        }
        return new V3(surface.Vertices[index]);
    }

    public void ReadVertices(int start, Span<V3> destination)
    {
        if (surface.Vertices is PagedList<double[]> paged)
        {
            paged.ReadRange(start, destination, vertices);
            return;
        }
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = new V3(surface.Vertices[start + index]);
        }
    }

    public void ReadFaces(int start, Span<SurfaceTriangleVertices> destination)
    {
        if (surface.Faces is PagedList<int[]> paged)
        {
            paged.ReadRange(start, destination, faces);
            return;
        }
        for (var index = 0; index < destination.Length; index++)
        {
            var face = surface.Faces[start + index];
            destination[index] = new SurfaceTriangleVertices(face[0], face[1], face[2]);
        }
    }

    public SurfaceTriangle Triangle(SurfaceTriangleVertices face) => new(Vertex(face.A), Vertex(face.B), Vertex(face.C));
}
