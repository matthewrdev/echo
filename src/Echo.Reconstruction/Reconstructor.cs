using Echo.Models;
using Echo.Compute;
using Echo.Telemetry;
using Echo.Geometry;
using Echo.Optimization;

namespace Echo.Reconstruction;

public sealed class Reconstructor : IReconstructor
{
    private readonly IBundleAdjuster bundleAdjuster;
    private readonly IVisualReconstructor visualReconstructor;
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace? trace;

    public Reconstructor(IBundleAdjuster bundleAdjuster, IVisualReconstructor visualReconstructor, IComputeService? compute = null, IPerformanceTrace? trace = null)
    {
        this.bundleAdjuster = bundleAdjuster;
        this.visualReconstructor = visualReconstructor;
        this.compute = compute;
        this.trace = trace;
    }

    public ReconstructionScene? TryReconcileOverlaps(ProjectData project, ReconstructionScene established,
        IReadOnlyList<ImagePair> pairs, CancellationToken cancellation, Action<OperationProgress>? progress = null)
    {
        var origin = established.Origin;
        if (origin == null || established.Bundle == null)
        {
            return null;
        }

        var priors = CameraPriorBuilder.Build(project);
        var positions = new V3[established.Cameras.Count];
        var weights = new double[positions.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            var position = priors[established.Cameras[index].ImageId].Position;
            if (position != null)
            {
                positions[index] = CameraGeometry.ToLocal(position, origin);
                weights[index] = 2 * CameraGeometry.Square(10 / Math.Max(10, position.UncertaintyMetres));
            }
        }

        // The position measurements stabilize scale while image matches constrain relative poses.
        if (weights.Count(weight => weight > 0) < positions.Length * .75)
        {
            return null;
        }

        var photos = project.Images.Where(photo => photo.Enabled).ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var verified = ImagePairVerifier.VerifyAll(photos, pairs, Math.Max(1, Environment.ProcessorCount - 2),
            cancellation, compute, project.Settings.ComputeBackend);
        var overlaps = CameraOverlapRefiner.Gather(established.Cameras, photos, verified, cancellation);
        var previous = CameraOverlapRefiner.Errors(overlaps, established.Cameras);
        if (!CameraOverlapRefiner.HasConflictingOverlaps(previous))
        {
            return null;
        }

        var candidate = new ReconstructionScene
        {
            Frame = established.Frame,
            Origin = established.Origin,
            Warnings = [.. established.Warnings],
            Unplaced = [.. established.Unplaced],
            Cameras = CameraOverlapRefiner.Refine(established.Cameras, overlaps, positions, weights, cancellation,
                iteration => progress?.Invoke(new OperationProgress("Reconcile camera overlaps",
                    $"Refining camera poses against verified photo overlaps · iteration {iteration + 1}")))
        };
        if (!CameraOverlapRefiner.ImprovesOverlaps(previous, CameraOverlapRefiner.Errors(overlaps, candidate.Cameras)))
        {
            return null;
        }

        try
        {
            // Rebuild tracks from the new camera poses so previously inconsistent observations can participate.
            bundleAdjuster.Refine(project, candidate, verified, cancellation, progress: progress);
        }
        catch (EchoException error) when (error.ExitCode == 5)
        {
            return null;
        }

        var preservesSupport = candidate.Bundle!.Observations >= established.Bundle.Observations * .95;
        var improvesOverlaps = CameraOverlapRefiner.ImprovesOverlaps(previous, CameraOverlapRefiner.Errors(overlaps, candidate.Cameras));
        return preservesSupport && improvesOverlaps ? candidate : null;
    }

    public ReconstructionScene Align(ProjectData project,
        List<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null, Action<OperationProgress>? progress = null)
    {
        var reporting = new ReconstructionProgress(progress, trace);
        var images = project.Images.Where(image => image.Enabled).ToDictionary(image => image.Id, StringComparer.Ordinal);
        var priors = CameraPriorBuilder.Build(project);

        // Reject descriptor aliases before they can bias any camera or point estimate.
        // Reuse only a certificate tied to the exact correspondences, flags,
        // threshold and implementation. Display flags alone are not authority.
        reporting.Report(new OperationProgress("Verify image geometry", "Verifying matched pairs"));
        pairs = ImagePairVerifier.VerifyAll(images, pairs, Math.Max(1, Environment.ProcessorCount - 2),
            cancellation, compute, project.Settings.ComputeBackend);

        if (CanSeedNadirFromMetadata(priors))
        {
            reporting.Report(new OperationProgress("Initialize cameras", "Seeding near-nadir cameras from capture positions and attitudes"));
            var nadir = TryAlignNadir(project, pairs, images, priors, cancellation, progress, reporting);
            if (nadir != null)
            {
                checkpoint?.Invoke(nadir);
                return nadir;
            }
        }

        var visual = visualReconstructor.TryAlignVerified(project, pairs, cancellation, checkpoint, progress);
        if (visual != null)
        {
            return visual;
        }

        return AlignFromPriors(project, pairs, images, priors, cancellation, checkpoint, progress, reporting);
    }

    private bool CanSeedNadirFromMetadata(Dictionary<string, CameraPrior> priors)
    {
        var nearNadir = priors.Values.Count(prior =>
        {
            var hasHeight = prior.Position?.Altitude.HasValue == true;
            var looksDown = prior.Attitude is { Pitch: <= -80 and >= -100 };
            return hasHeight && looksDown;
        });
        return nearNadir >= 4 && nearNadir * 10 >= priors.Count * 9;
    }

    private ReconstructionScene? TryAlignNadir(ProjectData project, List<ImagePair> pairs, Dictionary<string, Photo> images,
        Dictionary<string, CameraPrior> priors, CancellationToken cancellation, Action<OperationProgress>? progress,
        ReconstructionProgress reporting)
    {
        try
        {
            var candidate = AlignFromPriors(project, pairs, images, priors, cancellation, null, progress, reporting);
            var enoughSupport = candidate.Bundle is { FinalMedianError: <= 2.5 }
                && candidate.Points.Count >= 20 * candidate.Cameras.Count;
            if (enoughSupport)
            {
                candidate.Warnings.Add("Near-nadir camera alignment used image matches with GPS and attitude initialization.");
                return candidate;
            }
        }
        catch (EchoException error) when (error.ExitCode == 5)
        {
            reporting.Report(new OperationProgress("Initialize cameras", $"Near-nadir seed could not be verified: {error.Message}"));
        }

        return null;
    }

    private ReconstructionScene AlignFromPriors(ProjectData project, List<ImagePair> pairs, Dictionary<string, Photo> images,
        Dictionary<string, CameraPrior> priors, CancellationToken cancellation, Action<ReconstructionScene>? checkpoint,
        Action<OperationProgress>? progress, ReconstructionProgress reporting)
    {
        reporting.Report(new OperationProgress("Initialize cameras", "Building initial poses"));
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

        reporting.Report(new OperationProgress("Refine camera rotations", "Refining initial poses"));
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

        SparseTriangulator.Build(result, images, pairs, cancellation, project.Settings.ComputeBackend, checkpoint, compute, trace, progress);
        checkpoint?.Invoke(result);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var camera in result.Cameras)
        {
            registered.Add(camera.ImageId);
        }
        // Register photographs without GPS against existing image-to-cloud observations using calibrated DLT + RANSAC.
        for (var pass = 0; pass < 3; pass++)
        {
            reporting.Report(new OperationProgress("Register cameras", $"Registration pass {pass + 1} of 3 · {registered.Count:N0}/{images.Count:N0} cameras placed"));
            var added = false;
            foreach (var image in images.Values)
            {
                if (registered.Contains(image.Id))
                {
                    continue;
                }

                cancellation.ThrowIfCancellationRequested();
                var pose = CameraRegistrar.Register(image, pairs, result.Points, new Random(93), cancellation);
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

            SparseTriangulator.Build(result, images, pairs, cancellation, project.Settings.ComputeBackend, checkpoint, compute, trace, progress);
        }

        if (result.Cameras.Count >= 3)
        {
            reporting.Report(new OperationProgress("Bundle adjustment", "Refining cameras and tracks"));
            bundleAdjuster.Refine(project, result, pairs, cancellation, checkpoint, progress);
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

}
