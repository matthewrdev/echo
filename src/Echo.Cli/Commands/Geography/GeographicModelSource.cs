using Echo.Models;

namespace Echo.Cli.Commands.Geography;

internal sealed record GeographicModelSource(string Path, GeoPosition Origin, string? Coordinates = null, string? Atlas = null);
