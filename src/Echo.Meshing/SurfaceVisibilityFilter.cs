using Echo.Models;
using Echo.Compute;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceVisibilityFilter
{
    public static Surface Filter(Surface surface, ProjectData project, ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null, IComputeService? compute = null, MeshDepthContext? depthContext = null)
    {
        return Filter(surface, (depthContext ?? new MeshDepthContext(project, scene, maps)).Views, project.Settings.CloudDepthTolerance, cancellation, work, progress, compute, project.Settings.ComputeBackend);
    }

    public static Surface Filter(Surface surface, IReadOnlyList<DepthView> views,
        double relativeTolerance, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(relativeTolerance > 0) || !double.IsFinite(relativeTolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        }
        using var scratch = work?.CreateScope();
        var origin = views.Count == 0 ? default : views[0].Centre;
        using var acceleration = DepthCompute.Open(compute, backend, views, origin, cancellation);
        var accelerationFailed = false;
        var reader = new SurfaceGeometryReader(surface);
        var faces = new SurfaceTriangleVertices[4096];
        var triangles = new SurfaceTriangle[4096];
        var rejected = new bool[4096];
        var options = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };
        var result = work?.Surface() ?? new Surface();
        var writer = new SurfaceGeometryWriter(result);
        Span<int> output = stackalloc int[3];
        IList<int> vertices = scratch?.Filled(surface.Vertices.Count, -1, new IntRecordCodec(), cancellation) ?? Enumerable.Repeat(-1, surface.Vertices.Count).ToArray();
        var assigned = surface.SourceImages.Count == surface.Faces.Count;
        for (var start = 0; start < surface.Faces.Count; start += faces.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(faces.Length, surface.Faces.Count - start);
            reader.ReadFaces(start, faces.AsSpan(0, count));
            for (var index = 0; index < count; index++)
            {
                triangles[index] = reader.Triangle(faces[index]);
            }
            var accelerated = acceleration != null && !accelerationFailed;
            if (accelerated)
            {
                try
                {
                    SurfaceEvidenceCompute.Evaluate(acceleration!, triangles.AsSpan(0, count), origin, views.Count,
                        relativeTolerance, rejected.AsSpan(0, count), cancellation);
                }
                catch (ComputeException) when (backend == ComputeBackend.Auto)
                {
                    accelerationFailed = true;
                    accelerated = false;
                }
            }
            if (!accelerated)
            {
                Parallel.For(0, count, options, index =>
                    rejected[index] = SurfaceVisibilityEvidence.Contradicts(triangles[index], views, relativeTolerance));
            }
            for (var index = 0; index < count; index++)
            {
                if (rejected[index])
                {
                    continue;
                }
                var face = faces[index];

                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = corner == 0 ? face.A : corner == 1 ? face.B : face.C;
                    var mapped = vertices[vertex];
                    if (mapped < 0)
                    {
                        mapped = result.Vertices.Count;
                        var triangle = triangles[index];
                        writer.AddVertex(corner == 0 ? triangle.A : corner == 1 ? triangle.B : triangle.C);
                        vertices[vertex] = mapped;
                    }
                    output[corner] = mapped;
                }
                writer.AddFace(new SurfaceTriangleVertices(output[0], output[1], output[2]));
                if (assigned)
                {
                    result.SourceImages.Add(surface.SourceImages[start + index]);
                }
            }
            var message = accelerated ? "Checking triangles with Metal." : "Checking triangles against observed empty space.";
            progress?.Invoke(new("Visibility filtering", message, start + count, surface.Faces.Count, "triangles"));
        }
        return result;
    }
}
