using System.Text.Json.Serialization;
using Echo.Contracts;

namespace Echo.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ProjectData))]
[JsonSerializable(typeof(RunData))]
[JsonSerializable(typeof(Artifact))]
[JsonSerializable(typeof(ProcessingMetadata))]
[JsonSerializable(typeof(RunProcessingMetadata))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Photo))]
[JsonSerializable(typeof(List<Hint>))]
[JsonSerializable(typeof(FeatureSet))]
[JsonSerializable(typeof(ImagePair))]
[JsonSerializable(typeof(ReconstructionScene))]
[JsonSerializable(typeof(ReconstructionOverview))]
[JsonSerializable(typeof(DepthMap))]
[JsonSerializable(typeof(DenseCloudReport))]
[JsonSerializable(typeof(DenseCloudOverview))]
[JsonSerializable(typeof(FusedVolume))]
[JsonSerializable(typeof(Surface))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
public partial class CoreJsonReadContext : JsonSerializerContext
{
}
