using System.Text.Json.Serialization;

namespace Echo.Storage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FeatureArtifactHeader))]
[JsonSerializable(typeof(MatchArtifactHeader))]
[JsonSerializable(typeof(Echo.Models.MatchingIndexEntry))]
internal partial class MatchingStorageJsonContext : JsonSerializerContext
{
}
