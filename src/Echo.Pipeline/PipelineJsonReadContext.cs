using System.Text.Json.Serialization;

namespace Echo.Pipeline;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FusionGridMetadata))]
internal sealed partial class PipelineJsonReadContext : JsonSerializerContext
{
}
