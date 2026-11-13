using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Optimization;

namespace Echo.Reconstruction;

[Export(typeof(IReconstructor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class Reconstructor : IReconstructor
{
    private readonly IBundleAdjuster bundleAdjuster;
    private readonly ICameraGeometry cameraGeometry;
    private readonly IJsonCodec jsonCodec;
    private readonly ICameraRegistrar cameraRegistrar;
    private readonly IRotationRefiner rotationRefiner;

    [ImportingConstructor]
    public Reconstructor(ICameraRegistrar cameraRegistrar, IRotationRefiner rotationRefiner, IBundleAdjuster bundleAdjuster, ICameraGeometry cameraGeometry, IJsonCodec jsonCodec)
    {
        this.bundleAdjuster = bundleAdjuster;
        this.cameraGeometry = cameraGeometry;
        this.jsonCodec = jsonCodec;
        this.cameraRegistrar = cameraRegistrar;
        this.rotationRefiner = rotationRefiner;
    }

    public ReconstructionScene Align(ProjectData project,
        List<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null)
    {
        var images = project.Images.Where(i => i.Enabled).ToDictionary(i => i.Id);
        var effective = images.Values.Select(jsonCodec.Clone).ToDictionary(i => i.Id);
        foreach (var hint in project.Hints.Where(h => h.Enabled && h.Type == "camera" && effective.ContainsKey(h.ImageId)))
        {
            if (hint.Position != null)
            {
                effective[hint.ImageId].Gps = hint.Position;
            }

            if (hint.Attitude != null)
            {
                effective[hint.ImageId].Attitude = hint.Attitude;
            }
        }

        var result = new ReconstructionScene
        {
            Origin = effective.Values.FirstOrDefault(p => p.Gps != null && p.Attitude != null)?.Gps
        };
        foreach (var image in effective.Values.Where(p => p.Gps != null && p.Attitude != null))
        {
            result.Cameras.Add(new CameraPose
            {
                ImageId = image.Id,
                Centre = cameraGeometry.ToLocal(image.Gps!, result.Origin!).Array,
                Rotation = cameraGeometry.AttitudeRotation(image.Attitude!)
            });
        }

        if (result.Cameras.Count < 2)
        {
            result.Cameras.Clear();
            result.Origin = null;
            foreach (var pair in pairs.OrderByDescending(p => p.Matches.Count))
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = cameraGeometry.EstimateRelative(images[pair.A], images[pair.B], pair.Matches);
                if (relative == null)
                {
                    continue;
                }

                var rotation = cameraGeometry.Transpose(relative.Rotation);
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
                    Centre = (cameraGeometry.Rotate(rotation, new V3(relative.Translation)) * -1).Array
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
                foreach (var camera in result.Cameras.Skip(1))
                {
                    cancellation.ThrowIfCancellationRequested();
                    rotationRefiner.Refine(camera, result.Cameras, images, pairs);
                }
            }
        }

        TriangulateAll(result, images, pairs, cancellation);
        checkpoint?.Invoke(result);
        // Register photographs without GPS against existing image-to-cloud observations using calibrated DLT + RANSAC.
        for (var pass = 0; pass < 3; pass++)
        {
            var added = false;
            foreach (var image in images.Values.Where(i => result.Cameras.All(c => c.ImageId != i.Id)).ToArray())
            {
                cancellation.ThrowIfCancellationRequested();
                var pose = cameraRegistrar.Register(image, pairs, result.Points);
                if (pose == null)
                {
                    continue;
                }

                result.Cameras.Add(pose);
                added = true;
            }

            if (!added)
            {
                break;
            }

            TriangulateAll(result, images, pairs, cancellation);
            checkpoint?.Invoke(result);
        }

        if (result.Cameras.Count >= 3)
        {
            bundleAdjuster.Refine(project, result, pairs, cancellation, checkpoint);
        }
        else
        {
            result.Warnings.Add("Only two cameras: joint multi-view refinement was unavailable.");
        }

        result.Unplaced = images.Keys.Where(id => result.Cameras.All(c => c.ImageId != id)).ToList();
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
        var cameras = scene.Cameras.ToDictionary(c => c.ImageId);
        foreach (var pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!cameras.TryGetValue(pair.A, out var a) || !cameras.TryGetValue(pair.B, out var b))
            {
                continue;
            }

            foreach (var match in pair.Matches)
            {
                var triangulated = cameraGeometry.Triangulate(images[pair.A], images[pair.B], a, b, match, 18.0 * images[pair.A].Width / 1000);
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
