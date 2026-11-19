using Echo.Cli;
using Echo.Composition;
using Echo.Tests;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(ReconstructionTests).Assembly, typeof(Processor).Assembly, typeof(CommandApplication).Assembly,
    typeof(Echo.Compute.Metal.MetalComputeDevice).Assembly, typeof(Echo.Depth.Metal.MetalDepthCostAccelerator).Assembly, typeof(Echo.Texturing.Metal.MetalTexturePlacementAccelerator).Assembly);
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
    composition.Resolve<ReferenceDatasetTests>().Run(referenceTests, assetsRoot);
    return referenceTests.Finish();
}
if (args.Contains("--reconstruction", StringComparer.Ordinal))
{
    return composition.Resolve<ReconstructionTests>().Run();
}
var scaleTests = new TestSuite();
composition.Resolve<ColourGradingTests>().Run(scaleTests);
composition.Resolve<ColourGradingPipelineTests>().Run(scaleTests);
composition.Resolve<ReconstructionValidationTests>().Run(scaleTests);
if (args.Contains("--validation", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--gis", StringComparer.Ordinal))
{
    composition.Resolve<GisExportTests>().Run(scaleTests);
    composition.Resolve<GisCommandTests>().Run(scaleTests);
    composition.Resolve<GeographicFormatTests>().Run(scaleTests);
    return scaleTests.Finish();
}
composition.Resolve<MatchingStorageTests>().Run(scaleTests);
composition.Resolve<MatchingMigrationTests>().Run(scaleTests);
if (args.Contains("--matching-storage", StringComparer.Ordinal))
{
    composition.Resolve<FeatureSetCacheTests>().Run(scaleTests);
    composition.Resolve<ImageStageConcurrencyTests>().Run(scaleTests);
    return scaleTests.Finish();
}
composition.Resolve<ExclusionRegionTests>().Run(scaleTests);
composition.Resolve<CropPipelineTests>().Run(scaleTests);
if (args.Contains("--exclusions", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<ModelAssetCacheTests>().Run(scaleTests);
composition.Resolve<SemanticArtifactReuseTests>().Run(scaleTests);
if (args.Contains("--model-cache", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--compute", StringComparer.Ordinal))
{
    composition.Resolve<ComputeContractTests>().Run(scaleTests);
    composition.Resolve<ComputeTests>().Run(scaleTests, args.Contains("--require-metal", StringComparer.Ordinal));
    composition.Resolve<ComputePassTests>().Run(scaleTests);
    composition.Resolve<FusionIntegrationTests>().RunMetal(scaleTests);
    composition.Resolve<ComputeTextureTests>().Run(scaleTests);
    if (args.Contains("--benchmark", StringComparer.Ordinal))
    {
        composition.Resolve<ComputeBenchmarks>().Run();
    }
    return scaleTests.Finish();
}
if (args.Contains("--texture", StringComparer.Ordinal))
{
    composition.Resolve<TexturePriorityTests>().Run(scaleTests);
    composition.Resolve<TexturePriorityPipelineTests>().Run(scaleTests);
    composition.Resolve<TexturePackingTests>().Run(scaleTests, args.Contains("--require-metal", StringComparer.Ordinal));
    composition.Resolve<TextureParameterizationTests>().Run(scaleTests);
    composition.Resolve<TextureTests>().Run(scaleTests);
    composition.Resolve<TextureRunOptionsTests>().Run(scaleTests);
    return scaleTests.Finish();
}
composition.Resolve<FusionCandidateTests>().Run(scaleTests);
composition.Resolve<FusionIntegrationTests>().Run(scaleTests);
if (args.Contains("--fusion", StringComparer.Ordinal))
{
    composition.Resolve<FusionQualityTests>().Run(scaleTests);
    composition.Resolve<FusionSettingsTests>().Run(scaleTests);
    return scaleTests.Finish();
}
composition.Resolve<ProcessingTelemetryTests>().Run(scaleTests);
composition.Resolve<PerformanceTraceTests>().Run(scaleTests);
composition.Resolve<PerformanceDiagnosticsTests>().Run(scaleTests);
composition.Resolve<PerformanceTraceExportTests>().Run(scaleTests);
composition.Resolve<ProgressivePreviewTests>().Run(scaleTests);
if (args.Contains("--telemetry", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--depth", StringComparer.Ordinal))
{
    composition.Resolve<DepthNeighbourTests>().Run(scaleTests);
    composition.Resolve<SemanticQualityTests>().Run(scaleTests);
    composition.Resolve<DepthSkyFilterTests>().Run(scaleTests);
    composition.Resolve<StereoImageFilterTests>().Run(scaleTests);
    composition.Resolve<DepthBackendTests>().Run(scaleTests);
    composition.Resolve<DepthRecoveryTests>().Run(scaleTests);
    composition.Resolve<DepthSamplingTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--run-recovery", StringComparer.Ordinal))
{
    composition.Resolve<RunRecoveryTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--mesh", StringComparer.Ordinal))
{
    composition.Resolve<MeshingOptimizationTests>().Run(scaleTests);
    composition.Resolve<RunRecoveryTests>().Run(scaleTests);
    composition.Resolve<MeshQualityTests>().Run(scaleTests);
    composition.Resolve<PhotographSelectionTests>().Run(scaleTests);
    composition.Resolve<PagingTests>().Run(scaleTests);
    composition.Resolve<SurfaceCompletionTests>().Run(scaleTests);
    composition.Resolve<FinalHoleRepairTests>().Run(scaleTests);
    return scaleTests.Finish();
}
composition.Resolve<SceneStorageTests>().Run(scaleTests);
if (args.Contains("--scene-storage", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<MeshingOptimizationTests>().Run(scaleTests);
composition.Resolve<DiskIoTests>().Run(scaleTests);
if (args.Contains("--disk-io", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<GisExportTests>().Run(scaleTests);
composition.Resolve<GeographicFormatTests>().Run(scaleTests);
composition.Resolve<GisCommandTests>().Run(scaleTests);
composition.Resolve<RenderCommandTests>().Run(scaleTests);
composition.Resolve<PagingTests>().Run(scaleTests);
composition.Resolve<PagingSerializationTests>().Run(scaleTests);
if (args.Contains("--paging", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<MetadataTests>().Run(scaleTests);
composition.Resolve<JsonDecodingTests>().Run(scaleTests);
composition.Resolve<TextureRunOptionsTests>().Run(scaleTests);
composition.Resolve<PhotoDeduplicationTests>().Run(scaleTests);
composition.Resolve<ScaleSpaceFeatureTests>().Run(scaleTests);
composition.Resolve<GuidedMatchingTests>().Run(scaleTests);
composition.Resolve<AlignmentVerificationTests>().Run(scaleTests);
composition.Resolve<Matrix3InverterTests>().Run(scaleTests);
composition.Resolve<RunRecoveryTests>().Run(scaleTests);
composition.Resolve<DepthProgressTests>().Run(scaleTests);
composition.Resolve<DepthSkyFilterTests>().Run(scaleTests);
composition.Resolve<StereoImageFilterTests>().Run(scaleTests);
composition.Resolve<DepthBackendTests>().Run(scaleTests);
composition.Resolve<ProcessingMetadataTests>().Run(scaleTests);
composition.Resolve<SemanticQualityTests>().Run(scaleTests);
composition.Resolve<DepthRecoveryTests>().Run(scaleTests);
composition.Resolve<DepthSamplingTests>().Run(scaleTests);
composition.Resolve<DepthRangeTests>().Run(scaleTests);
composition.Resolve<SubjectDistanceTests>().Run(scaleTests);
composition.Resolve<SubjectDistanceRunOptionsTests>().Run(scaleTests);
composition.Resolve<DepthNeighbourTests>().Run(scaleTests);
composition.Resolve<StereoCostBatchTests>().Run(scaleTests);
composition.Resolve<GrayscaleSamplerTests>().Run(scaleTests);
composition.Resolve<ArtifactStorageTests>().Run(scaleTests);
composition.Resolve<ProjectManagementTests>().Run(scaleTests);
composition.Resolve<ProjectInspectionTests>().Run(scaleTests);
composition.Resolve<GaussianBlurTests>().Run(scaleTests);
composition.Resolve<FeatureSetCacheTests>().Run(scaleTests);
composition.Resolve<OrderedWorkTests>().Run(scaleTests);
composition.Resolve<ImageStageConcurrencyTests>().Run(scaleTests);
composition.Resolve<MeshQualityTests>().Run(scaleTests);
composition.Resolve<PhotographSelectionTests>().Run(scaleTests);
composition.Resolve<SurfaceCompletionTests>().Run(scaleTests);
composition.Resolve<FinalHoleRepairTests>().Run(scaleTests);
composition.Resolve<SurfaceIslandTests>().Run(scaleTests);
composition.Resolve<SkyGuardTests>().Run(scaleTests);
composition.Resolve<FusionQualityTests>().Run(scaleTests);
composition.Resolve<FusionSettingsTests>().Run(scaleTests);
composition.Resolve<TextureParameterizationTests>().Run(scaleTests);
composition.Resolve<TexturePackingTests>().Run(scaleTests);
composition.Resolve<TexturePriorityTests>().Run(scaleTests);
composition.Resolve<TexturePriorityPipelineTests>().Run(scaleTests);
composition.Resolve<ComputeContractTests>().Run(scaleTests);
composition.Resolve<ComputeTests>().Run(scaleTests, false);
composition.Resolve<ComputePassTests>().Run(scaleTests);
composition.Resolve<ComputeTextureTests>().Run(scaleTests);
return Math.Max(scaleTests.Finish(), composition.Resolve<ReconstructionTests>().Run());
