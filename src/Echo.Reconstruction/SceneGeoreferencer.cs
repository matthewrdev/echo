using Echo.Models;
using Echo.Geometry;

namespace Echo.Reconstruction;

internal static class SceneGeoreferencer
{
    public static bool TryApply(ReconstructionScene scene, IReadOnlyDictionary<string, CameraPrior> priors,
        Random random, CancellationToken cancellation)
    {
        var precise = scene.Cameras.Select(camera => priors[camera.ImageId]).Where(prior =>
            prior.VerticalUncertaintyMetres.HasValue && prior.Position != null
                && GeographicCoordinates.HasEllipsoidalHeight(prior.Position)).ToArray();
        var usePrecise = precise.Length >= 3;
        var origin = usePrecise ? precise[0].Position : priors.Values.Select(prior => prior.Position).FirstOrDefault(position => position != null);
        if (origin == null)
        {
            return false;
        }

        var positions = new List<PositionCorrespondence>();
        foreach (var camera in scene.Cameras)
        {
            var prior = priors[camera.ImageId];
            var position = prior.Position;
            if (position == null)
            {
                continue;
            }

            var trusted = prior.VerticalUncertaintyMetres.HasValue && GeographicCoordinates.HasEllipsoidalHeight(position);
            if (!usePrecise || trusted)
            {
                var tolerance = usePrecise ? Math.Max(1, 3 * Math.Max(position.UncertaintyMetres, prior.VerticalUncertaintyMetres!.Value))
                    : position.UncertaintyMetres;
                positions.Add(new PositionCorrespondence(new V3(camera.Centre),
                    CameraGeometry.ToLocal(position, origin), tolerance));
            }
        }

        var transform = SimilarityEstimator.Estimate(positions, random, cancellation);
        if (transform == null)
        {
            scene.Warnings.Add("GPS positions could not define a stable geographic frame; retained the visual reconstruction at arbitrary scale.");
            return false;
        }

        foreach (var camera in scene.Cameras)
        {
            camera.Centre = transform.Apply(new V3(camera.Centre)).Array;
            camera.Rotation = CameraGeometry.Multiply(transform.Rotation, camera.Rotation);
            camera.Source += "; GPS similarity reference";
        }

        foreach (var point in scene.Points)
        {
            cancellation.ThrowIfCancellationRequested();
            point.Position = transform.Apply(new V3(point.Position)).Array;
        }

        scene.Origin = origin;
        scene.Frame = "local: x east, y up, z south; metres when georeferenced";
        if (scene.Bundle != null)
        {
            scene.Bundle.MaximumCameraMovementMetres = positions
                .Select(position => (transform.Apply(position.Source) - position.Target).Length).Max();
        }

        if (origin.AltitudeDatum == "unknown")
        {
            scene.Warnings.Add("Absolute elevation datum is unknown; altitude offsets are approximate. Horizontal referencing is not survey validated.");
        }

        return true;
    }
}
