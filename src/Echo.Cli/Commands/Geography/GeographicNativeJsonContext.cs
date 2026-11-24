using System.Text.Json.Serialization;

namespace Echo.Cli.Commands.Geography;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GeographicRasterRequest))]
[JsonSerializable(typeof(GeographicProbeRequest))]
[JsonSerializable(typeof(GeographicProbeResult))]
[JsonSerializable(typeof(bool))]
internal sealed partial class GeographicNativeJsonContext : JsonSerializerContext
{
}
