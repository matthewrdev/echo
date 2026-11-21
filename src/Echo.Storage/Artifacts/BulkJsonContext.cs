using System.Text.Json.Serialization;

namespace Echo.Storage.Artifacts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BulkArtifactHeader))]
[JsonSerializable(typeof(Echo.Models.FusionResolutionReport))]
internal partial class BulkJsonContext : JsonSerializerContext
{
}
