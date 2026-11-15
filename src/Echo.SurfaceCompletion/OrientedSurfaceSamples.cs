using Echo.Contracts;
using Echo.Geometry;

namespace Echo.SurfaceCompletion;

public static class OrientedSurfaceSamples
{
    public static OrientedSurfaceSample[] Build(Surface mesh, ReconstructionScene scene, CancellationToken cancellation)
    {
        if (mesh.SourceImages.Count != mesh.Faces.Count)
        {
            throw new EchoException("Surface completion requires a verified photograph for each input face.", 5);
        }
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, camera => new V3(camera.Centre));
        var normals = new Dictionary<V3, V3>();
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
            foreach (var vertex in face)
            {
                var point = new V3(mesh.Vertices[vertex]);
                normals[point] = normals.GetValueOrDefault(point) + normal;
            }
        }
        return normals.Where(entry => entry.Value.Length > 1e-12)
            .Select(entry => new OrientedSurfaceSample(entry.Key, entry.Value.Unit)).ToArray();
    }
}
