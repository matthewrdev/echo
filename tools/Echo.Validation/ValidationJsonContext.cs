using System.Text.Json.Serialization;

namespace Echo.Validation;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReconstructionCase))]
[JsonSerializable(typeof(ValidationPhotoManifest))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(Dictionary<QualityMetric, double>))]
internal sealed partial class ValidationJsonContext : JsonSerializerContext
{
}
