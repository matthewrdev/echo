using Echo.Compute;
using Echo.Features;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Matching;
using Echo.Meshing;
using Echo.Models;
using Echo.Storage.Matching;

namespace Echo.ImageProjection;

internal sealed class ExternalImageRegistration : IExternalImageRegistration
{
    private readonly IFeatureExtractor extractor;
    private readonly IFeatureMatcher matcher;
    private readonly IFeatureArtifactStore features;
    private readonly IJsonCodec json;
    private readonly IComputeService compute;

    public ExternalImageRegistration(IFeatureExtractor extractor, IFeatureMatcher matcher, IFeatureArtifactStore features,
        IJsonCodec json, IComputeService compute)
    {
        this.extractor = extractor;
        this.matcher = matcher;
        this.features = features;
        this.json = json;
        this.compute = compute;
    }

    public RegistrationResult Register(ExternalRegistrationRequest request, CancellationToken cancellation)
    {
        if (request.Controls is { Count: > 0 } controls)
        {
            var matches = controls.Select(item => new RegistrationMatch(item.Pixel, new V3(item.Position))).ToArray();
            return Solve(request, matches, true, cancellation);
        }

        return Solve(request, FindMatches(request, cancellation), false, cancellation);
    }

    private RegistrationResult Solve(ExternalRegistrationRequest request, IReadOnlyList<RegistrationMatch> candidates,
        bool controls, CancellationToken cancellation)
    {
        var training = new List<RegistrationMatch>();
        var holdout = new List<RegistrationMatch>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var holdOut = !controls && index % 4 == 3;
            (holdOut ? holdout : training).Add(candidates[index]);
        }

        var camera = ExternalCameraEstimator.Estimate(request.Photo, training, request.FocalPixels, controls, cancellation)
            ?? throw new EchoException($"Could not register the external image from {candidates.Count} correspondences. Supply measured control points or select overlapping reference captures.", 3);
        var projection = new CameraProjector(request.Photo, camera.Pose);
        var threshold = ExternalCameraEstimator.ErrorThreshold(request.Photo);
        var validation = holdout.Select(match => ExternalCameraEstimator.Error(projection, match))
            .Where(error => error < threshold).Order().ToArray();
        if (!controls && validation.Length < 4)
        {
            throw new EchoException("The external camera fit did not agree with enough held-out image matches.", 3);
        }

        var visibility = CheckVisibility(request.Mesh, request.Photo, camera);
        var fit = new ImageCameraFit(controls ? "controlPoints" : "featureMatching", camera.Pose, camera.Inliers.Count,
            candidates.Count, camera.MedianError, visibility.MinimumDepth, controls ? null : validation.Length,
            controls ? null : holdout.Count, controls ? null : validation[validation.Length / 2], threshold,
            visibility.FirstHitSupport, visibility.SelectedHitSupport);
        return new RegistrationResult(fit, camera.Inliers);
    }

    private List<RegistrationMatch> FindMatches(ExternalRegistrationRequest request, CancellationToken cancellation)
    {
        var units = request.Run.Artifacts.TryGetValue("features", out var artifact)
            ? json.Read<Dictionary<string, string>>(request.Store.PathFor(artifact + "/units.json"))
            : new Dictionary<string, string>();
        var settings = request.Run.Snapshot.Settings;
        var options = new Settings
        {
            FeatureWidth = settings.FeatureWidth,
            FeatureLimit = Math.Min(settings.FeatureLimit, 8000),
            ScaleInvariantFeatures = settings.ScaleInvariantFeatures,
            DetectLines = false,
            ComputeBackend = settings.ComputeBackend,
            MatchRatioThreshold = .79,
            MatchDistanceThreshold = .75
        };
        var source = extractor.ExtractImage(request.ImagePath, request.Photo, options, cancellation);
        var captures = request.Run.Snapshot.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var posed = request.Scene.Cameras.Where(camera => captures.ContainsKey(camera.ImageId)).ToArray();
        if (request.ReferenceImageIds.Any(id => !posed.Any(camera => camera.ImageId == id)))
        {
            throw new EchoException("All reference image IDs must be registered in the selected run.", 2);
        }

        var selected = request.ReferenceImageIds.ToHashSet(StringComparer.Ordinal);
        var distinct = new Dictionary<ImageObservationKey, RegistrationMatch>();
        foreach (var pose in posed)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selected.Count != 0 && !selected.Contains(pose.ImageId))
            {
                continue;
            }

            var photo = captures[pose.ImageId];
            var capture = units.TryGetValue(photo.Id, out var unit)
                ? features.Read(request.Store.PathFor(unit + "/features.json"))
                : extractor.Extract(request.Store, photo, options, cancellation);
            capture.Lines.Clear();
            var pair = matcher.Match(source, capture, options, cancellation);
            var verified = ImagePairVerifier.Verify(request.Photo, photo, pair, cancellation, compute, options.ComputeBackend);
            foreach (var match in verified.Matches)
            {
                var hit = FirstHit(request.Mesh, photo, pose, match.B);
                if (hit is not { } surface)
                {
                    continue;
                }

                var key = new ImageObservationKey(request.Photo.Id, BitConverter.DoubleToInt64Bits(match.A[0]), BitConverter.DoubleToInt64Bits(match.A[1]));
                if (!distinct.TryGetValue(key, out var previous) || match.Score < previous.Score)
                {
                    distinct[key] = new RegistrationMatch(match.A, surface.Position, match.Score);
                }
            }
        }

        return distinct.Values.OrderBy(match => match.Score).ToList();
    }

    private RegistrationVisibility CheckVisibility(IMeshRaycaster mesh, Photo photo, EstimatedCamera camera)
    {
        var projection = new CameraProjector(photo, camera.Pose);
        var depths = camera.Inliers.Select(match => projection.Project(match.Position)?.Z ?? 0).Order().ToArray();
        var minimumDepth = depths[depths.Length / 10] * .55;
        var tolerance = depths[depths.Length / 2] * .025;
        var origin = new V3(camera.Pose.Centre);
        var firstSupport = 0;
        var blocked = 0;
        var selectedSupport = 0;
        foreach (var match in camera.Inliers)
        {
            var point = projection.Project(match.Position)!.Value;
            double[] pixel = [point.X, point.Y];
            var direction = CameraGeometry.Rotate(camera.Pose.Rotation, CameraGeometry.Ray(photo, pixel, camera.Pose));
            var first = mesh.FirstHit(origin, direction);
            if (first is { } firstHit && (firstHit.Position - match.Position).Length < tolerance)
            {
                firstSupport++;
            }

            if (first is { } near && near.Depth < minimumDepth)
            {
                blocked++;
            }

            var selected = mesh.FirstHit(origin, direction, minimumDepth);
            if (selected is { } selectedHit && (selectedHit.Position - match.Position).Length < tolerance)
            {
                selectedSupport++;
            }
        }

        var meaningful = Math.Max(4, camera.Inliers.Count * .15);
        if (blocked >= meaningful && selectedSupport >= firstSupport + meaningful)
        {
            return new RegistrationVisibility(minimumDepth, firstSupport, selectedSupport);
        }

        if (blocked >= meaningful && firstSupport < camera.Inliers.Count * .5)
        {
            throw new EchoException("Foreground geometry blocks the fitted surface. Supply control points or select closer reference captures.", 3);
        }

        return new RegistrationVisibility(null, firstSupport, firstSupport);
    }

    private MeshRayHit? FirstHit(IMeshRaycaster mesh, Photo photo, CameraPose pose, double[] pixel)
    {
        var direction = CameraGeometry.Rotate(pose.Rotation, CameraGeometry.Ray(photo, pixel, pose));
        return mesh.FirstHit(new V3(pose.Centre), direction);
    }
}
