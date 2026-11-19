using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Optimization;
using Echo.Telemetry;

namespace Echo.Reconstruction;

[Export(typeof(IVisualReconstructor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class VisualReconstructor : IVisualReconstructor
{
    private readonly IBundleAdjuster bundleAdjuster;
    private readonly IComputeService? compute;
    private readonly IPerformanceTrace? trace;

    [ImportingConstructor]
    public VisualReconstructor(IBundleAdjuster bundleAdjuster,
        [Import(AllowDefault = true)] IComputeService? compute = null,
        [Import(AllowDefault = true)] IPerformanceTrace? trace = null)
    {
        this.bundleAdjuster = bundleAdjuster;
        this.compute = compute;
        this.trace = trace;
    }

    public ReconstructionScene? TryAlignVerified(ProjectData project, List<ImagePair> pairs,
        CancellationToken cancellation, Action<ReconstructionScene>? checkpoint = null, Action<OperationProgress>? progress = null)
    {
        var reporting = new ReconstructionProgress(progress, trace);
        var priors = CameraPriorBuilder.Build(project);
        var geographicCameras = priors.Values.Count(prior => prior.Position != null);
        var insufficientReference = geographicCameras > 0 && geographicCameras < 3;
        if (priors.Count < 3 || insufficientReference)
        {
            return null;
        }

        var working = WorkingProject(project);
        var photos = working.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var calibrationGroups = photos.Values.ToDictionary(photo => photo.Id,
            photo => $"{photo.Camera}:{photo.Width}:{photo.Height}:{photo.FocalPixels:G8}", StringComparer.Ordinal);
        var scene = Seed(photos, priors, pairs, project.Settings.ComputeBackend, cancellation, progress);
        if (scene == null)
        {
            return null;
        }

        scene.Warnings.Add("Experimental: visual camera registration and shared focal refinement; GPS supplies a robust similarity reference. No lens-distortion calibration or survey control.");
        var registered = scene.Cameras.Select(camera => camera.ImageId).ToHashSet(StringComparer.Ordinal);
        checkpoint?.Invoke(scene);
        var registrationPass = 0;
        while (registered.Count < photos.Count)
        {
            cancellation.ThrowIfCancellationRequested();
            registrationPass++;
            reporting.Report(new OperationProgress("Register visual cameras", $"Registration pass {registrationPass} · {registered.Count:N0}/{photos.Count:N0} cameras placed"));
            var candidates = photos.Values.Where(photo => !registered.Contains(photo.Id)).ToArray();
            var poses = new CameraPose?[candidates.Length];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 16),
                CancellationToken = cancellation
            };
            // Every candidate sees the same completed bundle. Merge in photo order
            // so worker scheduling cannot change the next triangulation pass.
            Parallel.For(0, candidates.Length, options, index =>
            {
                poses[index] = CameraRegistrar.Register(candidates[index], pairs, scene.Points, new Random(93), cancellation);
            });

            var added = false;
            foreach (var pose in poses)
            {
                if (pose == null)
                {
                    continue;
                }
                scene.Cameras.Add(pose);
                registered.Add(pose.ImageId);
                added = true;
            }
            if (!added)
            {
                break;
            }

            SparseTriangulator.Build(scene, photos, pairs, cancellation, working.Settings.ComputeBackend,
                checkpoint, compute, trace, progress);
            bundleAdjuster.Refine(working, scene, pairs, cancellation, checkpoint, progress);
            UpdateCalibrations(photos, calibrationGroups, scene.Cameras);
        }

        if (scene.Cameras.Count < 3)
        {
            return null;
        }
        bundleAdjuster.Refine(working, scene, pairs, cancellation, checkpoint, progress);
        foreach (var id in photos.Keys)
        {
            if (!registered.Contains(id))
            {
                scene.Unplaced.Add(id);
            }
        }

        reporting.Report(new OperationProgress("Reference visual reconstruction", "Aligning the visual frame to consistent GPS positions"));
        var georeferenced = SceneGeoreferencer.TryApply(scene, priors, new Random(817), cancellation);
        if (geographicCameras > 0 && !georeferenced)
        {
            // A straight flight line cannot fix rotation around that line.
            // Preserve the metadata-based metric fallback instead of silently
            // feeding arbitrary visual units to metric depth settings.
            return null;
        }
        checkpoint?.Invoke(scene);
        return scene;
    }

    private ReconstructionScene? Seed(Dictionary<string, Photo> photos,
        Dictionary<string, CameraPrior> priors, List<ImagePair> pairs, ComputeBackend backend,
        CancellationToken cancellation, Action<OperationProgress>? progress)
    {
        var reporting = new ReconstructionProgress(progress, trace);
        reporting.Report(new OperationProgress("Select visual seed", "Finding a supported two-view reconstruction"));
        var candidates = pairs.Where(pair => pair.Matches.Count >= 100)
            .OrderByDescending(pair => SeedScore(pair, priors));
        foreach (var pair in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            var relative = CameraGeometry.EstimateRelative(photos[pair.A], photos[pair.B], pair.Matches, cancellation);
            if (relative == null)
            {
                continue;
            }

            var rotation = CameraGeometry.Transpose(relative.Rotation);
            var scene = new ReconstructionScene
            {
                Frame = "unreferenced local frame, arbitrary scale",
                Cameras =
                [
                    new CameraPose { ImageId = pair.A, Source = "visual-seed" },
                    new CameraPose
                    {
                        ImageId = pair.B,
                        Source = "visual-seed",
                        Rotation = rotation,
                        Centre = (CameraGeometry.Rotate(rotation, new V3(relative.Translation)) * -1).Array
                    }
                ]
            };
            SparseTriangulator.Build(scene, photos, [pair], cancellation, backend, null, compute, trace, progress);
            if (scene.Points.Count >= 100)
            {
                return scene;
            }
        }
        return null;
    }

    private double SeedScore(ImagePair pair, IReadOnlyDictionary<string, CameraPrior> priors)
    {
        var a = priors[pair.A].Position;
        var b = priors[pair.B].Position;
        // GPS is useful for preferring a baseline, but does not define the
        // camera poses or discard tracks before visual registration.
        var baseline = a == null || b == null ? 1 : Math.Min(20, CameraGeometry.ToLocal(a, b).Length);
        return pair.Matches.Count * baseline;
    }

    private ProjectData WorkingProject(ProjectData project)
    {
        var working = new ProjectData { Settings = project.Settings, Hints = project.Hints };
        foreach (var photo in project.Images)
        {
            if (!photo.Enabled)
            {
                continue;
            }
            // Calibration evolves during incremental registration. Keep those
            // estimates local; imported metadata and the run snapshot are immutable.
            working.Images.Add(new Photo
            {
                Id = photo.Id,
                Camera = photo.Camera,
                Width = photo.Width,
                Height = photo.Height,
                FocalPixels = photo.FocalPixels,
                Gps = photo.Gps,
                Attitude = photo.Attitude
            });
        }
        return working;
    }

    private void UpdateCalibrations(Dictionary<string, Photo> photos,
        Dictionary<string, string> groups, List<CameraPose> cameras)
    {
        var estimates = cameras.GroupBy(camera => groups[camera.ImageId])
            .ToDictionary(group => group.Key, group => group.Select(camera => camera.FocalPixels).Order().ElementAt(group.Count() / 2));
        foreach (var photo in photos.Values)
        {
            if (estimates.TryGetValue(groups[photo.Id], out var focal))
            {
                photo.FocalPixels = focal;
            }
        }
    }
}
