using System.Text.Json.Serialization;

namespace Echo.Storage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BulkArtifactHeader))]
[JsonSerializable(typeof(Echo.Contracts.FusionResolutionReport))]
internal partial class BulkJsonContext : JsonSerializerContext
{
}
