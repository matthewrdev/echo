namespace Echo.Metadata;

public sealed class AltitudeReference
{
    public string Datum { get; set; } = "unknown";
    public string Source { get; set; } = "";
    public string Evidence { get; set; } = "";
}
