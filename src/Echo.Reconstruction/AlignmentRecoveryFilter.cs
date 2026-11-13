using System.ComponentModel.Composition;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Reconstruction;

[Export(typeof(IAlignmentRecoveryFilter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class AlignmentRecoveryFilter : IAlignmentRecoveryFilter
{
    private readonly ICameraGeometry geometry;
    private readonly IImagePairVerifier verifier;

    [ImportingConstructor]
    public AlignmentRecoveryFilter(ICameraGeometry geometry, IImagePairVerifier verifier)
    {
        this.geometry = geometry;
        this.verifier = verifier;
    }

    public List<ImagePair> Filter(ProjectData project, ReconstructionScene scene, IReadOnlyList<ImagePair> originalPairs,
        IReadOnlyList<ImagePair> proposedPairs, CancellationToken cancellation, Action<int, int>? progress = null)
    {
        var photos = new Dictionary<string, Photo>(StringComparer.Ordinal);
        var cameras = new Dictionary<string, CameraPose>(StringComparer.Ordinal);
        var landmarks = new Dictionary<ImageObservationKey, V3>();
        foreach (var photo in project.Images)
        {
            photos.Add(photo.Id, photo);
        }
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        foreach (var point in scene.Points)
        {
            var position = new V3(point.Position);
            landmarks.TryAdd(Key(point.ImageA, point.PixelA), position);
            landmarks.TryAdd(Key(point.ImageB, point.PixelB), position);
        }
        var output = new List<ImagePair>(originalPairs.Count);
        var reportClock = Stopwatch.StartNew();
        for (var index = 0; index < originalPairs.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (reportClock.ElapsedMilliseconds >= 750 || index == originalPairs.Count - 1)
            {
                progress?.Invoke(index + 1, originalPairs.Count);
                reportClock.Restart();
            }
            var original = originalPairs[index];
            var proposed = proposedPairs[index];
            var left = photos[original.A];
            var right = photos[original.B];
            var filtered = verifier.Verify(left, right, original, cancellation);
            output.Add(filtered);
            if (!cameras.TryGetValue(original.A, out var leftCamera) || !cameras.TryGetValue(original.B, out var rightCamera))
            {
                continue;
            }
            var accepted = new HashSet<MatchObservationKey>();
            foreach (var match in filtered.Matches)
            {
                accepted.Add(new MatchObservationKey(Key(original.A, match.A), Key(original.B, match.B)));
            }
            foreach (var match in proposed.Matches)
            {
                var leftKey = Key(original.A, match.A);
                var rightKey = Key(original.B, match.B);
                var key = new MatchObservationKey(leftKey, rightKey);
                if (accepted.Contains(key))
                {
                    continue;
                }
                // Epipolar agreement alone cannot distinguish repeated ledges. If an
                // endpoint already belongs to a multi-view landmark, require the new
                // observation to agree with that independently reconstructed point.
                if (landmarks.TryGetValue(leftKey, out var leftPoint) && !Agrees(right, rightCamera, leftPoint, match.B))
                {
                    continue;
                }
                if (landmarks.TryGetValue(rightKey, out var rightPoint) && !Agrees(left, leftCamera, rightPoint, match.A))
                {
                    continue;
                }
                var tolerance = 2.0 * Math.Min(left.Width, right.Width) / 1000;
                if (geometry.Triangulate(left, right, leftCamera, rightCamera, match, tolerance) == null)
                {
                    continue;
                }
                accepted.Add(key);
                filtered.Matches.Add(match);
            }
        }
        return output;
    }

    private bool Agrees(Photo photo, CameraPose camera, V3 point, double[] pixel)
    {
        var projected = geometry.ProjectPoint(photo, camera, point);
        if (projected == null)
        {
            return false;
        }
        var dx = projected.Value.X - pixel[0];
        var dy = projected.Value.Y - pixel[1];
        var tolerance = 2.0 * photo.Width / 1000;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    private ImageObservationKey Key(string image, double[] pixel)
    {
        return new ImageObservationKey(image, (long)Math.Round(pixel[0] * 10), (long)Math.Round(pixel[1] * 10));
    }
}
