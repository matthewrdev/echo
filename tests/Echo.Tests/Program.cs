using Echo.Cli;
using Echo.Composition;
using Echo.Migrate;
using Echo.Tests;
using Echo.Validation;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
new ProcessingServices().Register(services);
new CliServices().Register(services);
new MigrationServices().Register(services);
new ValidationServices().Register(services);
new TestServices().Register(services);
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

var referenceAssetsIndex = Array.IndexOf(args, "--reference-assets");
if (referenceAssetsIndex >= 0)
{
    var assetsRoot = referenceAssetsIndex + 1 < args.Length ? args[referenceAssetsIndex + 1] : Environment.GetEnvironmentVariable("ECHO_REFERENCE_ASSETS_ROOT");
    if (string.IsNullOrWhiteSpace(assetsRoot))
    {
        Console.Error.WriteLine("Pass --reference-assets PATH or set ECHO_REFERENCE_ASSETS_ROOT to the downloaded 3d-assets directory.");
        return 2;
    }
    var referenceTests = new TestSuite();
    provider.GetRequiredService<ReferenceDatasetTests>().Run(referenceTests, assetsRoot);
    return referenceTests.Finish();
}
if (args.Contains("--reconstruction", StringComparer.Ordinal))
{
    return provider.GetRequiredService<ReconstructionTests>().Run();
}
var scaleTests = new TestSuite();
provider.GetRequiredService<ColourGradingTests>().Run(scaleTests);
provider.GetRequiredService<ColourGradingPipelineTests>().Run(scaleTests);
provider.GetRequiredService<ReconstructionValidationTests>().Run(scaleTests);
if (args.Contains("--validation", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--gis", StringComparer.Ordinal))
{
    provider.GetRequiredService<GisExportTests>().Run(scaleTests);
    provider.GetRequiredService<GisCommandTests>().Run(scaleTests);
    provider.GetRequiredService<GeographicFormatTests>().Run(scaleTests);
    return scaleTests.Finish();
}
provider.GetRequiredService<MatchingStorageTests>().Run(scaleTests);
provider.GetRequiredService<MatchingMigrationTests>().Run(scaleTests);
if (args.Contains("--matching-storage", StringComparer.Ordinal))
{
    provider.GetRequiredService<FeatureSetCacheTests>().Run(scaleTests);
    provider.GetRequiredService<ImageStageConcurrencyTests>().Run(scaleTests);
    return scaleTests.Finish();
}
provider.GetRequiredService<ExclusionRegionTests>().Run(scaleTests);
provider.GetRequiredService<CropPipelineTests>().Run(scaleTests);
if (args.Contains("--exclusions", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
provider.GetRequiredService<ModelAssetCacheTests>().Run(scaleTests);
provider.GetRequiredService<SemanticArtifactReuseTests>().Run(scaleTests);
if (args.Contains("--model-cache", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--compute", StringComparer.Ordinal))
{
    provider.GetRequiredService<ComputeContractTests>().Run(scaleTests);
    provider.GetRequiredService<ComputeTests>().Run(scaleTests, args.Contains("--require-metal", StringComparer.Ordinal));
    provider.GetRequiredService<ComputePassTests>().Run(scaleTests);
    provider.GetRequiredService<FusionIntegrationTests>().RunMetal(scaleTests);
    provider.GetRequiredService<ComputeTextureTests>().Run(scaleTests);
    if (args.Contains("--benchmark", StringComparer.Ordinal))
    {
        provider.GetRequiredService<ComputeBenchmarks>().Run();
    }
    return scaleTests.Finish();
}
if (args.Contains("--texture", StringComparer.Ordinal))
{
    provider.GetRequiredService<TexturePriorityTests>().Run(scaleTests);
    provider.GetRequiredService<TexturePriorityPipelineTests>().Run(scaleTests);
    provider.GetRequiredService<TexturePackingTests>().Run(scaleTests, args.Contains("--require-metal", StringComparer.Ordinal));
    provider.GetRequiredService<TextureParameterizationTests>().Run(scaleTests);
    provider.GetRequiredService<TextureTests>().Run(scaleTests);
    provider.GetRequiredService<TextureRunOptionsTests>().Run(scaleTests);
    return scaleTests.Finish();
}
provider.GetRequiredService<FusionCandidateTests>().Run(scaleTests);
provider.GetRequiredService<FusionIntegrationTests>().Run(scaleTests);
if (args.Contains("--fusion", StringComparer.Ordinal))
{
    provider.GetRequiredService<FusionQualityTests>().Run(scaleTests);
    provider.GetRequiredService<FusionSettingsTests>().Run(scaleTests);
    return scaleTests.Finish();
}
provider.GetRequiredService<ProcessingTelemetryTests>().Run(scaleTests);
provider.GetRequiredService<PerformanceTraceTests>().Run(scaleTests);
provider.GetRequiredService<PerformanceDiagnosticsTests>().Run(scaleTests);
provider.GetRequiredService<PerformanceTraceExportTests>().Run(scaleTests);
provider.GetRequiredService<ProgressivePreviewTests>().Run(scaleTests);
if (args.Contains("--telemetry", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--depth", StringComparer.Ordinal))
{
    provider.GetRequiredService<DepthNeighbourTests>().Run(scaleTests);
    provider.GetRequiredService<SemanticQualityTests>().Run(scaleTests);
    provider.GetRequiredService<DepthSkyFilterTests>().Run(scaleTests);
    provider.GetRequiredService<StereoImageFilterTests>().Run(scaleTests);
    provider.GetRequiredService<DepthBackendTests>().Run(scaleTests);
    provider.GetRequiredService<DepthRecoveryTests>().Run(scaleTests);
    provider.GetRequiredService<DepthSamplingTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--run-recovery", StringComparer.Ordinal))
{
    provider.GetRequiredService<RunRecoveryTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--mesh", StringComparer.Ordinal))
{
    provider.GetRequiredService<MeshingOptimizationTests>().Run(scaleTests);
    provider.GetRequiredService<RunRecoveryTests>().Run(scaleTests);
    provider.GetRequiredService<MeshQualityTests>().Run(scaleTests);
    provider.GetRequiredService<PhotographSelectionTests>().Run(scaleTests);
    provider.GetRequiredService<PagingTests>().Run(scaleTests);
    provider.GetRequiredService<SurfaceCompletionTests>().Run(scaleTests);
    provider.GetRequiredService<FinalHoleRepairTests>().Run(scaleTests);
    return scaleTests.Finish();
}
provider.GetRequiredService<SceneStorageTests>().Run(scaleTests);
if (args.Contains("--scene-storage", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
provider.GetRequiredService<MeshingOptimizationTests>().Run(scaleTests);
provider.GetRequiredService<DiskIoTests>().Run(scaleTests);
if (args.Contains("--disk-io", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
provider.GetRequiredService<GisExportTests>().Run(scaleTests);
provider.GetRequiredService<GeographicFormatTests>().Run(scaleTests);
provider.GetRequiredService<GisCommandTests>().Run(scaleTests);
provider.GetRequiredService<RenderCommandTests>().Run(scaleTests);
provider.GetRequiredService<PagingTests>().Run(scaleTests);
provider.GetRequiredService<PagingSerializationTests>().Run(scaleTests);
if (args.Contains("--paging", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
provider.GetRequiredService<MetadataTests>().Run(scaleTests);
provider.GetRequiredService<HeifImportTests>().Run(scaleTests);
provider.GetRequiredService<JsonDecodingTests>().Run(scaleTests);
provider.GetRequiredService<TextureRunOptionsTests>().Run(scaleTests);
provider.GetRequiredService<PhotoDeduplicationTests>().Run(scaleTests);
provider.GetRequiredService<ScaleSpaceFeatureTests>().Run(scaleTests);
provider.GetRequiredService<GuidedMatchingTests>().Run(scaleTests);
provider.GetRequiredService<AlignmentVerificationTests>().Run(scaleTests);
provider.GetRequiredService<Matrix3InverterTests>().Run(scaleTests);
provider.GetRequiredService<RunRecoveryTests>().Run(scaleTests);
provider.GetRequiredService<DepthProgressTests>().Run(scaleTests);
provider.GetRequiredService<DepthSkyFilterTests>().Run(scaleTests);
provider.GetRequiredService<StereoImageFilterTests>().Run(scaleTests);
provider.GetRequiredService<DepthBackendTests>().Run(scaleTests);
provider.GetRequiredService<ProcessingMetadataTests>().Run(scaleTests);
provider.GetRequiredService<SemanticQualityTests>().Run(scaleTests);
provider.GetRequiredService<DepthRecoveryTests>().Run(scaleTests);
provider.GetRequiredService<DepthSamplingTests>().Run(scaleTests);
provider.GetRequiredService<DepthRangeTests>().Run(scaleTests);
provider.GetRequiredService<SubjectDistanceTests>().Run(scaleTests);
provider.GetRequiredService<SubjectDistanceRunOptionsTests>().Run(scaleTests);
provider.GetRequiredService<DepthNeighbourTests>().Run(scaleTests);
provider.GetRequiredService<StereoCostBatchTests>().Run(scaleTests);
provider.GetRequiredService<GrayscaleSamplerTests>().Run(scaleTests);
provider.GetRequiredService<ArtifactStorageTests>().Run(scaleTests);
provider.GetRequiredService<ProjectManagementTests>().Run(scaleTests);
provider.GetRequiredService<ProjectInspectionTests>().Run(scaleTests);
provider.GetRequiredService<GaussianBlurTests>().Run(scaleTests);
provider.GetRequiredService<FeatureSetCacheTests>().Run(scaleTests);
provider.GetRequiredService<OrderedWorkTests>().Run(scaleTests);
provider.GetRequiredService<ImageStageConcurrencyTests>().Run(scaleTests);
provider.GetRequiredService<MeshQualityTests>().Run(scaleTests);
provider.GetRequiredService<PhotographSelectionTests>().Run(scaleTests);
provider.GetRequiredService<SurfaceCompletionTests>().Run(scaleTests);
provider.GetRequiredService<FinalHoleRepairTests>().Run(scaleTests);
provider.GetRequiredService<SurfaceIslandTests>().Run(scaleTests);
provider.GetRequiredService<SkyGuardTests>().Run(scaleTests);
provider.GetRequiredService<FusionQualityTests>().Run(scaleTests);
provider.GetRequiredService<FusionSettingsTests>().Run(scaleTests);
provider.GetRequiredService<TextureParameterizationTests>().Run(scaleTests);
provider.GetRequiredService<TexturePackingTests>().Run(scaleTests);
provider.GetRequiredService<TexturePriorityTests>().Run(scaleTests);
provider.GetRequiredService<TexturePriorityPipelineTests>().Run(scaleTests);
provider.GetRequiredService<ComputeContractTests>().Run(scaleTests);
provider.GetRequiredService<ComputeTests>().Run(scaleTests, false);
provider.GetRequiredService<ComputePassTests>().Run(scaleTests);
provider.GetRequiredService<ComputeTextureTests>().Run(scaleTests);
return Math.Max(scaleTests.Finish(), provider.GetRequiredService<ReconstructionTests>().Run());
