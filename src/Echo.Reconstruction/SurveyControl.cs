using Echo.Geometry;
using Echo.Models;

namespace Echo.Reconstruction;

public sealed class SurveyControl : ISurveyControl
{
    public SurveyControlReport Apply(ProjectData project, ReconstructionScene scene, CancellationToken cancellation)
    {
        var hints = project.Hints.Where(hint => hint.Enabled && hint.Type is "ground-control" or "survey-check").ToArray();
        var controls = hints.Where(hint => hint.Type == "ground-control").ToArray();
        var positions = hints.ToDictionary(hint => hint.Id, hint => SurveyPosition(hint), StringComparer.Ordinal);
        if (positions.Values.Select(position => new { position.Latitude, position.Longitude, position.Altitude }).Distinct().Count() != positions.Count)
        {
            throw new EchoException("Survey points must have distinct coordinates. A control cannot also be used as an independent check.", 2);
        }

        var origin = controls.Length == 0 ? scene.Origin : positions[controls[0].Id];
        if (origin == null || !GeographicCoordinates.HasEllipsoidalHeight(origin))
        {
            throw new EchoException("Survey checks require ground control or an alignment with known WGS84 ellipsoidal heights.", 2);
        }

        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        var triangulated = hints.ToDictionary(hint => hint.Id, hint => Locate(hint, photos, cameras), StringComparer.Ordinal);
        SimilarityTransform? transform = null;
        if (controls.Length > 0)
        {
            var correspondences = controls.Where(hint => triangulated[hint.Id] != null).Select(hint =>
                new PositionCorrespondence(triangulated[hint.Id]!.Point, CameraGeometry.ToLocal(positions[hint.Id], origin),
                    positions[hint.Id].UncertaintyMetres)).ToArray();
            transform = SimilarityEstimator.Estimate(correspondences, new Random(2309), cancellation)
                ?? throw new EchoException("Ground control needs at least three consistent non-collinear points triangulated across registered photos.", 5);

            TransformScene(scene, origin, transform, cancellation);
        }

        var report = BuildReport(hints, positions, triangulated, origin, transform, null, cancellation);
        if (transform != null && report.Points.Count(point => point.WithinControlTolerance) < 3)
        {
            throw new EchoException("Fewer than three ground-control points fit within their tolerances.", 5);
        }

        return report;
    }

    public SurveyControlReport Measure(ProjectData project, ReconstructionScene scene, IReadOnlySet<string> controlIds,
        CancellationToken cancellation)
    {
        var origin = scene.Origin ?? throw new EchoException("Survey measurements need an established geographic frame.", 5);
        var hints = project.Hints.Where(hint => hint.Enabled && hint.Type is "ground-control" or "survey-check").ToArray();
        var positions = hints.ToDictionary(hint => hint.Id, hint => SurveyPosition(hint), StringComparer.Ordinal);
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        var triangulated = hints.ToDictionary(hint => hint.Id, hint => Locate(hint, photos, cameras), StringComparer.Ordinal);
        return BuildReport(hints, positions, triangulated, origin, null, controlIds, cancellation);
    }

    private SurveyControlReport BuildReport(Hint[] hints, Dictionary<string, GeoPosition> positions,
        Dictionary<string, Triangulated?> triangulated, GeoPosition origin, SimilarityTransform? transform,
        IReadOnlySet<string>? controlIds, CancellationToken cancellation)
    {
        var residuals = new List<SurveyPointResidual>(hints.Length);
        foreach (var hint in hints)
        {
            cancellation.ThrowIfCancellationRequested();
            var measured = triangulated[hint.Id];
            if (measured == null)
            {
                residuals.Add(new SurveyPointResidual(hint.Id, hint.Type, "Point could not be triangulated", null, null, null, null, false, controlIds?.Contains(hint.Id) ?? false));
                continue;
            }

            var point = transform?.Apply(measured.Point) ?? measured.Point;
            var delta = point - CameraGeometry.ToLocal(positions[hint.Id], origin);
            var withinTolerance = hint.Type == "ground-control" && delta.Length <= positions[hint.Id].UncertaintyMetres;
            var status = hint.Type == "ground-control" && !withinTolerance ? "Outside control tolerance" : "Measured";
            var used = controlIds?.Contains(hint.Id) ?? withinTolerance;
            residuals.Add(new SurveyPointResidual(hint.Id, hint.Type, status, measured.Error, delta.X, -delta.Z, delta.Y, withinTolerance, used));
        }

        var checks = residuals.Where(point => point.Type == "survey-check" && point.UpMetres.HasValue).ToArray();
        var horizontal = checks.Length == 0 ? (double?)null : Math.Sqrt(checks.Average(point =>
            CameraGeometry.Square(point.EastMetres!.Value) + CameraGeometry.Square(point.NorthMetres!.Value)));
        var vertical = checks.Length == 0 ? (double?)null : Math.Sqrt(checks.Average(point => CameraGeometry.Square(point.UpMetres!.Value)));
        var reference = controlIds != null ? "Ground-control constrained bundle adjustment"
            : transform == null ? "Existing geographic alignment" : "Ground control similarity fit";
        return new SurveyControlReport(reference,
            residuals, horizontal, vertical);
    }

    private GeoPosition SurveyPosition(Hint hint)
    {
        var position = hint.Position;
        if (position == null || !GeographicCoordinates.HasEllipsoidalHeight(position))
        {
            throw new EchoException($"Survey point {hint.Id} requires a WGS84 ellipsoidal position.", 2);
        }

        position.Validate();
        return position;
    }

    private Triangulated? Locate(Hint hint, IReadOnlyDictionary<string, Photo> photos, IReadOnlyDictionary<string, CameraPose> cameras)
    {
        var observationsPresent = hint.OtherImageId != null && hint.Pixel is { Length: 2 } && hint.OtherPixel is { Length: 2 };
        if (!observationsPresent || !cameras.TryGetValue(hint.ImageId, out var first)
            || !cameras.TryGetValue(hint.OtherImageId!, out var second))
        {
            return null;
        }

        return CameraGeometry.Triangulate(photos[first.ImageId], photos[second.ImageId], first, second,
            new Match { A = hint.Pixel!, B = hint.OtherPixel!, Manual = true });
    }

    private void TransformScene(ReconstructionScene scene, GeoPosition origin, SimilarityTransform transform, CancellationToken cancellation)
    {
        foreach (var camera in scene.Cameras)
        {
            camera.Centre = transform.Apply(new V3(camera.Centre)).Array;
            camera.Rotation = CameraGeometry.Multiply(transform.Rotation, camera.Rotation);
        }

        foreach (var point in scene.Points)
        {
            cancellation.ThrowIfCancellationRequested();
            point.Position = transform.Apply(new V3(point.Position)).Array;
        }

        scene.Origin = origin;
        scene.Frame = "local: x east, y up, z south; metres when georeferenced";
        scene.Warnings.Add("Survey control sets the geographic frame. See survey-control.json for control and independent check residuals.");
    }
}
