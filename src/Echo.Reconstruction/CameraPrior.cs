using Echo.Models;

namespace Echo.Reconstruction;

internal readonly record struct CameraPrior
{
    public CameraPrior(Photo photo)
    {
        // Alignment needs only camera priors, not a serialized copy of every EXIF tag.
        var position = photo.Gps;
        Position = position == null ? null : new GeoPosition
        {
            Latitude = position.Latitude,
            Longitude = position.Longitude,
            Altitude = position.Altitude,
            AltitudeDatum = position.AltitudeDatum,
            UncertaintyMetres = position.UncertaintyMetres
        };
        Attitude = photo.Attitude;
        VerticalUncertaintyMetres = null;
    }

    public GeoPosition? Position
    {
        get; init;
    }
    public double? VerticalUncertaintyMetres
    {
        get; init;
    }
    public Attitude? Attitude
    {
        get; init;
    }
}
