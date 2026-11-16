using System.Text.Json.Serialization;

namespace Echo.Benchmarks;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FusionPlanningMeasurement))]
[JsonSerializable(typeof(FusionPlanningProgressRecord))]
internal sealed partial class FusionPlanningBenchmarkJsonContext : JsonSerializerContext
{
}
