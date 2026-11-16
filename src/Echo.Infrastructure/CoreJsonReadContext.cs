using System.Text.Json.Serialization;
using Echo.Contracts;

namespace Echo.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(AlignmentPreview))]
[JsonSerializable(typeof(FusionPreview))]
[JsonSerializable(typeof(PerformanceTraceDocument))]
[JsonSerializable(typeof(List<PerformanceTraceSummary>))]
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
[JsonSerializable(typeof(SemanticMap))]
[JsonSerializable(typeof(bool[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<Photo>))]
[JsonSerializable(typeof(FeatureSet[]))]
[JsonSerializable(typeof(Dictionary<string, IslandTrimMode>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(SortedDictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(Dictionary<string, System.Text.Json.JsonElement>))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(byte[]))]
public partial class CoreJsonReadContext : JsonSerializerContext
{
}
