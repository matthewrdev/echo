using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Meshing.Geometry;

internal sealed class SurfaceGeometryWriter
{
    private readonly Surface surface;
    private readonly V3RecordCodec vertices = new();
    private readonly SurfaceTriangleVerticesCodec faces = new();

    public SurfaceGeometryWriter(Surface surface)
    {
        this.surface = surface;
    }

    public int AddVertex(V3 point)
    {
        var index = surface.Vertices.Count;
        if (surface.Vertices is PagedList<double[]> paged)
        {
            paged.AddRecord(point, vertices);
        }
        else
        {
            surface.Vertices.Add(point.Array);
        }
        return index;
    }

    public void AddFace(SurfaceTriangleVertices face)
    {
        if (surface.Faces is PagedList<int[]> paged)
        {
            paged.AddRecord(face, faces);
        }
        else
        {
            surface.Faces.Add([face.A, face.B, face.C]);
        }
    }
}
