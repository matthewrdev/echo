using System.Text.Json.Serialization;

namespace Echo.Benchmarks;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FusionPlanningMeasurement))]
[JsonSerializable(typeof(FusionPlanningProgressRecord))]
[JsonSerializable(typeof(FusionIntegrationMeasurement))]
[JsonSerializable(typeof(FusionResumeMeasurement))]
[JsonSerializable(typeof(MeshIndexMeasurement))]
internal sealed partial class FusionPlanningBenchmarkJsonContext : JsonSerializerContext
{
}
