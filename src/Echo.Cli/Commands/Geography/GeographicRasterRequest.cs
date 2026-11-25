namespace Echo.Cli.Commands.Geography;

internal sealed record GeographicRasterRequest(GeographicModelSource Source, string Directory, int? Size = null);
