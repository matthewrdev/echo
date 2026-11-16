using System.Text.Json.Serialization;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Benchmarks;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(AlignmentMeasurement))]
[JsonSerializable(typeof(MetalDepthFixtureInfo))]
[JsonSerializable(typeof(RelativePose))]
[JsonSerializable(typeof(FundamentalMatrixEstimate))]
[JsonSerializable(typeof(List<DepthMap>))]
[JsonSerializable(typeof(DepthMap[]))]
[JsonSerializable(typeof(List<FeatureSet>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(BundleMeasurement))]
[JsonSerializable(typeof(CloudMeasurement))]
[JsonSerializable(typeof(CoreMeasurement))]
[JsonSerializable(typeof(CrossPassMeasurement))]
[JsonSerializable(typeof(DepthCoverageMeasurement))]
[JsonSerializable(typeof(DepthDiagnosticMeasurement))]
[JsonSerializable(typeof(DepthMeasurement))]
[JsonSerializable(typeof(DepthSkyReport))]
[JsonSerializable(typeof(DepthSkyResult))]
[JsonSerializable(typeof(FeatureMeasurement))]
[JsonSerializable(typeof(MetalDepthComparison))]
[JsonSerializable(typeof(PairSelectionCheck))]
[JsonSerializable(typeof(PairSelectionMeasurement))]
[JsonSerializable(typeof(PairVerificationDecision))]
[JsonSerializable(typeof(PairVerificationReport))]
[JsonSerializable(typeof(SceneStorageComparison))]
internal partial class BenchmarksJsonContext : JsonSerializerContext
{
}
