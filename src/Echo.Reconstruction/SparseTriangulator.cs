using Echo.Contracts;
using Echo.Compute;
using Echo.Geometry;
using Echo.Telemetry;

namespace Echo.Reconstruction;

internal static class SparseTriangulator
{
    public static void Build(ReconstructionScene scene,
        Dictionary<string, Photo> images,
        List<ImagePair> pairs,
        CancellationToken cancellation, ComputeBackend backend, Action<ReconstructionScene>? checkpoint, IComputeService? compute, IPerformanceTrace? trace)
    {
        trace?.Operation(new OperationProgress("Triangulation", "Constructing sparse points"));
        scene.Points.Clear();
        var cameras = new Dictionary<string, CameraPose>(scene.Cameras.Count, StringComparer.Ordinal);
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        foreach (var pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!cameras.TryGetValue(pair.A, out var a) || !cameras.TryGetValue(pair.B, out var b))
            {
                continue;
            }

            var tolerance = 18.0 * images[pair.A].Width / 1000;
            var accelerated = TriangulationCompute.TryCalculate(compute, backend, images[pair.A], images[pair.B], a, b, pair.Matches, tolerance, cancellation);
            for (var matchIndex = 0; matchIndex < pair.Matches.Count; matchIndex++)
            {
                var match = pair.Matches[matchIndex];
                var triangulated = accelerated == null ? CameraGeometry.Triangulate(images[pair.A], images[pair.B], a, b, match, tolerance) : accelerated[matchIndex];
                if (triangulated != null)
                {
                    scene.Points.Add(new CloudPoint
                    {
                        Position = triangulated.Point.Array,
                        ImageA = pair.A,
                        ImageB = pair.B,
                        PixelA = match.A,
                        PixelB = match.B,
                        Error = triangulated.Error
                    });
                }
            }
            checkpoint?.Invoke(scene);
        }
    }
}
