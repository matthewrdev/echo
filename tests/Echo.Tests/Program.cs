using Echo.Cli;
using Echo.Composition;
using Echo.Tests;
using Echo.Pipeline;

using var composition = new MefComposition(typeof(ReconstructionTests).Assembly, typeof(Processor).Assembly, typeof(CommandApplication).Assembly,
    typeof(Echo.Compute.Metal.MetalComputeDevice).Assembly, typeof(Echo.Depth.Metal.MetalDepthCostAccelerator).Assembly, typeof(Echo.Texturing.Metal.MetalTexturePlacementAccelerator).Assembly);
var scaleTests = new TestSuite();
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
    composition.Resolve<TexturePackingTests>().Run(scaleTests, args.Contains("--require-metal", StringComparer.Ordinal));
    composition.Resolve<TextureParameterizationTests>().Run(scaleTests);
    composition.Resolve<TextureTests>().Run(scaleTests);
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
composition.Resolve<ProgressivePreviewTests>().Run(scaleTests);
if (args.Contains("--telemetry", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
if (args.Contains("--depth", StringComparer.Ordinal))
{
    composition.Resolve<SemanticQualityTests>().Run(scaleTests);
    composition.Resolve<DepthSkyFilterTests>().Run(scaleTests);
    composition.Resolve<StereoImageFilterTests>().Run(scaleTests);
    composition.Resolve<DepthBackendTests>().Run(scaleTests);
    composition.Resolve<DepthRecoveryTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--run-recovery", StringComparer.Ordinal))
{
    composition.Resolve<RunRecoveryTests>().Run(scaleTests);
    return scaleTests.Finish();
}
if (args.Contains("--mesh", StringComparer.Ordinal))
{
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
composition.Resolve<DiskIoTests>().Run(scaleTests);
if (args.Contains("--disk-io", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<GisExportTests>().Run(scaleTests);
composition.Resolve<GisCommandTests>().Run(scaleTests);
composition.Resolve<PagingTests>().Run(scaleTests);
composition.Resolve<PagingSerializationTests>().Run(scaleTests);
if (args.Contains("--paging", StringComparer.Ordinal))
{
    return scaleTests.Finish();
}
composition.Resolve<MetadataTests>().Run(scaleTests);
composition.Resolve<JsonDecodingTests>().Run(scaleTests);
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
composition.Resolve<DepthRangeTests>().Run(scaleTests);
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
composition.Resolve<ComputeContractTests>().Run(scaleTests);
composition.Resolve<ComputeTests>().Run(scaleTests, false);
composition.Resolve<ComputePassTests>().Run(scaleTests);
composition.Resolve<ComputeTextureTests>().Run(scaleTests);
return Math.Max(scaleTests.Finish(), composition.Resolve<ReconstructionTests>().Run());
