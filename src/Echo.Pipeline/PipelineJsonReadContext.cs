using System.Text.Json.Serialization;
using Echo.Depth;

namespace Echo.Pipeline;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FusionGridMetadata))]
[JsonSerializable(typeof(SubjectDistancePlan))]
internal sealed partial class PipelineJsonReadContext : JsonSerializerContext
{
}
