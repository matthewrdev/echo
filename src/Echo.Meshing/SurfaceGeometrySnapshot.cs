using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

// A shared, immutable view of the indexed geometry. Ray queries need neither
// page-cache locks nor per-triangle arrays. Existing vertices/faces must remain
// unchanged for the index's lifetime; newly filled faces use its additions list.
internal sealed class SurfaceGeometrySnapshot : IDisposable
{
    private V3[] vertices = [];
    private SurfaceTriangleVertices[] faces = [];
    private readonly PageMemoryReservation? reservation;

    public SurfaceGeometrySnapshot(Surface mesh, CancellationToken cancellation)
    {
        var pagedFaces = mesh.Faces as PagedList<int[]>;
        var pagedVertices = mesh.Vertices as PagedList<double[]>;
        if (pagedFaces == null && pagedVertices == null)
        {
            return;
        }
        var bytes = checked(mesh.Vertices.Count * 24L + mesh.Faces.Count * 12L + 128);
        reservation = pagedFaces != null ? pagedFaces.TryReserveMemory(bytes) : pagedVertices!.TryReserveMemory(bytes);
        if (reservation == null)
        {
            return;
        }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var vertices = new V3[mesh.Vertices.Count];
            var faces = new SurfaceTriangleVertices[mesh.Faces.Count];
            var vertexCodec = new V3RecordCodec();
            var faceCodec = new SurfaceTriangleVerticesCodec();
            for (var start = 0; start < vertices.Length; start += 4096)
            {
                cancellation.ThrowIfCancellationRequested();
                var batch = vertices.AsSpan(start, Math.Min(4096, vertices.Length - start));
                if (pagedVertices != null)
                {
                    pagedVertices.ReadRange(start, batch, vertexCodec);
                }
                else
                {
                    for (var index = 0; index < batch.Length; index++)
                    {
                        batch[index] = new V3(mesh.Vertices[start + index]);
                    }
                }
            }
            for (var start = 0; start < faces.Length; start += 4096)
            {
                cancellation.ThrowIfCancellationRequested();
                var batch = faces.AsSpan(start, Math.Min(4096, faces.Length - start));
                if (pagedFaces != null)
                {
                    pagedFaces.ReadRange(start, batch, faceCodec);
                }
                else
                {
                    for (var index = 0; index < batch.Length; index++)
                    {
                        var face = mesh.Faces[start + index];
                        batch[index] = new SurfaceTriangleVertices(face[0], face[1], face[2]);
                    }
                }
            }
            this.vertices = vertices;
            this.faces = faces;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public bool IsAvailable => reservation != null;

    public SurfaceTriangle Triangle(int index)
    {
        var face = faces[index];
        return new SurfaceTriangle(vertices[face.A], vertices[face.B], vertices[face.C]);
    }

    public void Dispose()
    {
        vertices = [];
        faces = [];
        reservation?.Dispose();
    }
}
