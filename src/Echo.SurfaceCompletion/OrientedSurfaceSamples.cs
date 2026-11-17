using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

public static class OrientedSurfaceSamples
{
    public static OrientedSurfaceSample[] Build(Surface mesh, ReconstructionScene scene, CancellationToken cancellation)
        => BuildRecords(mesh, scene, cancellation).ToArray();

    public static IReadOnlyList<OrientedSurfaceSample> BuildRecords(Surface mesh, ReconstructionScene scene, CancellationToken cancellation, WorkBuffers? work = null)
    {
        if (mesh.SourceImages.Count != mesh.Faces.Count)
        {
            throw new EchoException("Surface completion requires a verified photograph for each input face.", 5);
        }
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, camera => new V3(camera.Centre));
        using var scratch = work?.CreateScope(preferResident: true);
        var normals = new BinaryHashIndex<V3, V3>(mesh.Vertices.Count, new Vector3RecordCodec(), new Vector3RecordCodec(), scratch, cancellation);
        foreach (var index in Enumerable.Range(0, mesh.Faces.Count))
        {
            cancellation.ThrowIfCancellationRequested();
            var face = mesh.Faces[index];
            var a = new V3(mesh.Vertices[face[0]]);
            var b = new V3(mesh.Vertices[face[1]]);
            var c = new V3(mesh.Vertices[face[2]]);
            var normal = (b - a).Cross(c - a);
            if (!cameras.TryGetValue(mesh.SourceImages[index], out var camera))
            {
                throw new EchoException("A surface photograph has no reconstructed camera.", 5);
            }
            if (normal.Dot(camera - (a + b + c) / 3) < 0)
            {
                normal *= -1;
            }
            ReadOnlySpan<V3> points = [a, b, c];
            foreach (var point in points)
            {
                normals.TryGetValue(point, out var accumulated);
                normals[point] = accumulated + normal;
            }
        }
        IList<OrientedSurfaceSample> samples = work?.List(new OrientedSurfaceSampleCodec()) ?? new List<OrientedSurfaceSample>();
        foreach (var entry in normals)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.Value.Length > 1e-12)
            {
                samples.Add(new OrientedSurfaceSample(entry.Key, entry.Value.Unit));
            }
        }
        return (IReadOnlyList<OrientedSurfaceSample>)samples;
    }
}
