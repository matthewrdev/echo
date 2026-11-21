namespace Echo.Models;

public sealed class GeoPosition
{
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public double? Altitude
    {
        get; set;
    }
    public string AltitudeDatum { get; set; } = "unknown";
    public double UncertaintyMetres { get; set; } = 10;

    public void Validate()
    {
        var invalidCoordinates = !double.IsFinite(Latitude)
            || !double.IsFinite(Longitude)
            || Math.Abs(Latitude) > 85
            || Math.Abs(Longitude) > 180;
        var invalidAltitude = Altitude.HasValue && !double.IsFinite(Altitude.Value);
        var invalidUncertainty = !double.IsFinite(UncertaintyMetres) || UncertaintyMetres <= 0;
        var invalidMeasurement = invalidCoordinates || invalidAltitude || invalidUncertainty;

        if (invalidMeasurement)
        {
            throw new EchoException("Invalid geographic measurement (v0.1 supports latitudes within ±85°).", 2);
        }
    }
}
