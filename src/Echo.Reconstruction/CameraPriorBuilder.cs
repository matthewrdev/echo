using Echo.Models;

namespace Echo.Reconstruction;

internal static class CameraPriorBuilder
{
    public static Dictionary<string, CameraPrior> Build(ProjectData project)
    {
        var priors = new Dictionary<string, CameraPrior>(StringComparer.Ordinal);
        foreach (var image in project.Images)
        {
            if (image.Enabled)
            {
                priors.Add(image.Id, new CameraPrior(image));
            }
        }
        foreach (var hint in project.Hints)
        {
            var cameraHint = hint.Enabled && hint.Type == "camera";
            if (!cameraHint || !priors.TryGetValue(hint.ImageId, out var prior))
            {
                continue;
            }
            var position = hint.Position;
            priors[hint.ImageId] = prior with
            {
                Position = position == null ? prior.Position : new GeoPosition
                {
                    Latitude = position.Latitude,
                    Longitude = position.Longitude,
                    Altitude = position.Altitude,
                    AltitudeDatum = position.AltitudeDatum,
                    UncertaintyMetres = position.UncertaintyMetres
                },
                Attitude = hint.Attitude ?? prior.Attitude
            };
        }
        return priors;
    }
}
