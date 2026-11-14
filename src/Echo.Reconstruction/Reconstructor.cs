using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Optimization;

namespace Echo.Reconstruction;

[Export(typeof(IReconstructor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class Reconstructor : IReconstructor
{
    private readonly IBundleAdjuster bundleAdjuster;

    [ImportingConstructor]
    public Reconstructor(IBundleAdjuster bundleAdjuster)
    {
        this.bundleAdjuster = bundleAdjuster;
    }

    public ReconstructionScene Align(ProjectData project,
        List<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null)
    {
        var images = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        var priors = new Dictionary<string, CameraPrior>(project.Images.Count, StringComparer.Ordinal);
        foreach (var image in project.Images)
        {
            if (!image.Enabled)
            {
                continue;
            }
            images.Add(image.Id, image);
            priors.Add(image.Id, new CameraPrior(image));
        }
        foreach (var hint in project.Hints)
        {
            var cameraHint = hint.Enabled && hint.Type == "camera";
            if (!cameraHint || !priors.TryGetValue(hint.ImageId, out var prior))
            {
                continue;
            }
            priors[hint.ImageId] = prior with
            {
                Position = hint.Position ?? prior.Position,
                Attitude = hint.Attitude ?? prior.Attitude
            };
        }

        // Reject descriptor aliases before they can bias any camera or point estimate.
        // Recheck legacy artifacts too; serialized display flags are not solver authority.
        var verifiedPairs = new List<ImagePair>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (images.TryGetValue(pair.A, out var left) && images.TryGetValue(pair.B, out var right))
            {
                verifiedPairs.Add(ImagePairVerifier.Verify(left, right, pair, cancellation));
            }
        }
        pairs = verifiedPairs;

        var result = new ReconstructionScene();
        foreach (var image in images.Values)
        {
            var prior = priors[image.Id];
            if (prior.Position == null || prior.Attitude == null)
            {
                continue;
            }
            result.Origin ??= prior.Position;
            result.Cameras.Add(new CameraPose
            {
                ImageId = image.Id,
                Centre = CameraGeometry.ToLocal(prior.Position, result.Origin).Array,
                Rotation = CameraGeometry.AttitudeRotation(prior.Attitude)
            });
        }

        if (result.Cameras.Count < 2)
        {
            result.Cameras.Clear();
            result.Origin = null;
            foreach (var pair in pairs.OrderByDescending(p => p.Matches.Count))
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = CameraGeometry.EstimateRelative(images[pair.A], images[pair.B], pair.Matches);
                if (relative == null)
                {
                    continue;
                }

                var rotation = CameraGeometry.Transpose(relative.Rotation);
                result.Cameras.Add(new CameraPose
                {
                    ImageId = pair.A,
                    Source = "visual-seed"
                });
                result.Cameras.Add(new CameraPose
                {
                    ImageId = pair.B,
                    Source = "visual-seed",
                    Rotation = rotation,
                    Centre = (CameraGeometry.Rotate(rotation, new V3(relative.Translation)) * -1).Array
                });
                result.Frame = "unreferenced local frame, arbitrary scale";
                break;
            }
        }

        if (result.Cameras.Count < 2)
        {
            throw new EchoException("Could not initialise two cameras. Supply GPS + camera attitude hints or stronger overlapping imagery.", 5);
        }

        result.Warnings.Add("Experimental: joint pose/point/focal refinement with GPS soft priors; no lens-distortion calibration or survey control.");
        if (result.Origin?.AltitudeDatum == "unknown")
        {
            result.Warnings.Add("Absolute elevation datum is unknown; altitude offsets are approximate. Horizontal referencing is not survey validated.");
        }

        if (result.Origin != null)
        {
            // Optimise only small rotation corrections: noisy GPS must not be disguised as precise visual poses.
            for (var pass = 0; pass < 3; pass++)
            {
                for (var index = 1; index < result.Cameras.Count; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    RotationRefiner.Refine(result.Cameras[index], result.Cameras, images, pairs);
                }
            }
        }

        TriangulateAll(result, images, pairs, cancellation);
        if (result.Cameras.Count == 2)
        {
            checkpoint?.Invoke(result);
        }
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var camera in result.Cameras)
        {
            registered.Add(camera.ImageId);
        }
        // Register photographs without GPS against existing image-to-cloud observations using calibrated DLT + RANSAC.
        for (var pass = 0; pass < 3; pass++)
        {
            var added = false;
            foreach (var image in images.Values)
            {
                if (registered.Contains(image.Id))
                {
                    continue;
                }
                cancellation.ThrowIfCancellationRequested();
                var pose = CameraRegistrar.Register(image, pairs, result.Points);
                if (pose == null)
                {
                    continue;
                }

                result.Cameras.Add(pose);
                registered.Add(image.Id);
                added = true;
            }

            if (!added)
            {
                break;
            }

            TriangulateAll(result, images, pairs, cancellation);
        }

        if (result.Cameras.Count >= 3)
        {
            bundleAdjuster.Refine(project, result, pairs, cancellation, checkpoint);
        }
        else
        {
            result.Warnings.Add("Only two cameras: joint multi-view refinement was unavailable.");
        }

        foreach (var id in images.Keys)
        {
            if (!registered.Contains(id))
            {
                result.Unplaced.Add(id);
            }
        }
        if (result.Points.Count < 12)
        {
            throw new EchoException($"Only {result.Points.Count} triangulated points. Check overlap, attitude conventions and focal estimates before generating surfaces.", 5);
        }

        return result;
    }

    private void TriangulateAll(ReconstructionScene scene,
        Dictionary<string, Photo> images,
        List<ImagePair> pairs,
        CancellationToken cancellation)
    {
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

            foreach (var match in pair.Matches)
            {
                var triangulated = CameraGeometry.Triangulate(images[pair.A], images[pair.B], a, b, match, 18.0 * images[pair.A].Width / 1000);
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
        }
    }
}
