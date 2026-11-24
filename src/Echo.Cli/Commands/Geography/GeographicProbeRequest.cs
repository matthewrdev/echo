namespace Echo.Cli.Commands.Geography;

internal sealed record GeographicProbeRequest(GeographicModelSource Source, string Latitude, string Longitude,
    string? Altitude, string Accuracy);
