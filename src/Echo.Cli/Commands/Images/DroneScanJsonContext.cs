using System.Text.Json.Serialization;

namespace Echo.Cli.Commands.Images;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DroneScanRequest))]
[JsonSerializable(typeof(DroneScanGroup[]))]
internal sealed partial class DroneScanJsonContext : JsonSerializerContext
{
}
