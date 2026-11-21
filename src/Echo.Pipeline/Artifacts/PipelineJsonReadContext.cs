using System.Text.Json.Serialization;
using Echo.Depth;
using Echo.Pipeline.Fusion;

namespace Echo.Pipeline.Artifacts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FusionGridMetadata))]
[JsonSerializable(typeof(SubjectDistancePlan))]
internal sealed partial class PipelineJsonReadContext : JsonSerializerContext
{
}
