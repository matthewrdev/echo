using System.Text.Json.Serialization;
using Echo.Contracts;

namespace Echo.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(CommandError))]
[JsonSerializable(typeof(RuntimeInfo))]
[JsonSerializable(typeof(List<RunSummary>))]
[JsonSerializable(typeof(ImageSetSummary[]))]
[JsonSerializable(typeof(DeduplicationResult))]
[JsonSerializable(typeof(DoctorReport))]
[JsonSerializable(typeof(GlbComparisonResult))]
[JsonSerializable(typeof(ImageSetSummary))]
[JsonSerializable(typeof(PhotoImportResult))]
[JsonSerializable(typeof(PipelinePlanResult))]
[JsonSerializable(typeof(PipelineResetResult))]
[JsonSerializable(typeof(ProjectDeletionResult))]
[JsonSerializable(typeof(ProjectOpenResult))]
[JsonSerializable(typeof(ProjectRevisionResult))]
[JsonSerializable(typeof(RunCancellationResult))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
