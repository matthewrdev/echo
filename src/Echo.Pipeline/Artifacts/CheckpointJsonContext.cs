using System.Text.Json.Serialization;
using Echo.Depth;
using Echo.Models;
using Echo.Pipeline.Cloud;

namespace Echo.Pipeline.Artifacts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DenseCloudInputs))]
[JsonSerializable(typeof(SubjectDistancePlan))]
[JsonSerializable(typeof(TexturePriorityPainting))]
[JsonSerializable(typeof(ColourGrade))]
[JsonSerializable(typeof(Photo))]
[JsonSerializable(typeof(Hint))]
[JsonSerializable(typeof(ExclusionRegion))]
[JsonSerializable(typeof(GpsPolygon))]
[JsonSerializable(typeof(ComputeBackend))]
[JsonSerializable(typeof(IslandTrimMode))]
internal partial class CheckpointJsonContext : JsonSerializerContext
{
}
