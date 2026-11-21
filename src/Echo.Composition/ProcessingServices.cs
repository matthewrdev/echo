using Echo.Compute.Metal;
using Echo.Compute;
using Echo.DenseCloud;
using Echo.Depth.Metal;
using Echo.Depth;
using Echo.Export;
using Echo.Features;
using Echo.Fusion;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Lines;
using Echo.Logging;
using Echo.Matching;
using Echo.Meshing;
using Echo.Metadata;
using Echo.Optimization;
using Echo.Pipeline.Alignment;
using Echo.Pipeline.Artifacts;
using Echo.Pipeline.Cloud;
using Echo.Pipeline.DepthEstimation;
using Echo.Pipeline.Exporting;
using Echo.Pipeline.Features;
using Echo.Pipeline.Fusion;
using Echo.Pipeline.Matching;
using Echo.Pipeline.Meshes;
using Echo.Pipeline.Runs;
using Echo.Pipeline.Semantics;
using Echo.Pipeline.Texture;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Reconstruction;
using Echo.SceneSemantics;
using Echo.Simplification;
using Echo.Storage.Artifacts;
using Echo.Storage.Matching;
using Echo.Storage.Resources;
using Echo.Storage;
using Echo.SurfaceCompletion;
using Echo.Telemetry;
using Echo.Texturing.Charts;
using Echo.Texturing.Layout;
using Echo.Texturing.Metal;
using Echo.Texturing.Rasterization;
using Echo.Texturing.Seams;
using Echo.Texturing.Serialization;
using Echo.Texturing;
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Composition;

public sealed class ProcessingServices
{
    public void Register(IServiceCollection services)
    {
        new LoggingServices().Register(services);

        services.AddSingleton<IComputeService, ComputeService>();
        services.AddSingleton<IComputeDevice, MetalComputeDevice>();

        services.AddSingleton<ICloudBuilder, CloudBuilder>();
        services.AddSingleton<ICloudExecutionPlanner, CloudExecutionPlanner>();

        services.AddSingleton<ICliffDepthRecovery, CliffDepthRecovery>();
        services.AddSingleton<IDepthEstimator, DepthEstimator>();
        services.AddSingleton<IDepthExecutionPlanner, DepthExecutionPlanner>();
        services.AddSingleton<IDepthCostAccelerator, MetalDepthCostAccelerator>();

        services.AddSingleton<IGeoPackageWriter, GeoPackageWriter>();
        services.AddSingleton<IGisWriter, GisWriter>();
        services.AddSingleton<IGlbWriter, GlbWriter>();
        services.AddSingleton<ILasWriter, LasWriter>();
        services.AddSingleton<IRedpointGeoreferenceWriter, RedpointGeoreferenceWriter>();
        services.AddSingleton<ITilesetWriter, TilesetWriter>();

        services.AddSingleton<IFeatureExtractor, FeatureExtractor>();

        services.AddSingleton<IDepthFusion, DepthFusion>();
        services.AddSingleton<IFusionResourcePlanner, FusionResourcePlanner>();

        services.AddSingleton<IRasterImageService, RasterImageService>();

        services.AddSingleton<IJsonReadMetadata, CoreJsonReadMetadata>();
        services.AddSingleton<IFileOperations, FileOperations>();
        services.AddSingleton<IJsonCodec, JsonCodec>();

        services.AddSingleton<ILineDetector, LineDetector>();

        services.AddSingleton<IFeatureMatcher, FeatureMatcher>();
        services.AddSingleton<IGuidedFeatureMatcher, GuidedFeatureMatcher>();

        services.AddSingleton<IMeshBuilder, MeshBuilder>();

        services.AddSingleton<MetadataReader>();
        services.AddSingleton<IMetadataReader>(provider => provider.GetRequiredService<MetadataReader>());
        services.AddSingleton<IMetadataPayloadReader>(provider => provider.GetRequiredService<MetadataReader>());

        services.AddSingleton<IBundleAdjuster, BundleAdjuster>();

        services.AddSingleton<IAlignmentPreviewReader, AlignmentPreviewReader>();
        services.AddSingleton<IAlignmentPreviewWriter, AlignmentPreviewWriter>();
        services.AddSingleton<IArtifactStorageService, ArtifactStorageService>();
        services.AddSingleton<ICheckpointStore, CheckpointStore>();
        services.AddSingleton<ICliffRecoveryArtifacts, CliffRecoveryArtifacts>();
        services.AddSingleton<IFeatureSetCacheFactory, FeatureSetCacheFactory>();
        services.AddSingleton<IGisExportService, GisExportService>();
        services.AddSingleton<IImagePairSelector, ImagePairSelector>();
        services.AddSingleton<IImageWorkPlanner, ImageWorkPlanner>();
        services.AddSingleton<IOrderedWorkExecutor, OrderedWorkExecutor>();
        services.AddSingleton<IJsonReadMetadata, PipelineJsonReadMetadata>();
        services.AddSingleton<IPlyWriter, PlyWriter>();
        services.AddSingleton<IProcessor, Processor>();
        services.AddSingleton<IProjectInspector, ProjectInspector>();
        services.AddSingleton<IRunContextFactory, RunContextFactory>();
        services.AddSingleton<IRunStateRecovery, RunStateRecovery>();
        services.AddSingleton<ISceneSemanticArtifacts, SceneSemanticArtifacts>();
        services.AddSingleton<IStageResetService, StageResetService>();

        services.AddSingleton<IPhotoDeduplicator, PhotoDeduplicator>();
        services.AddSingleton<IPhotoDeletionService, PhotoDeletionService>();
        services.AddSingleton<IProjectManagementService, ProjectManagementService>();
        services.AddSingleton<IProjectStoreFactory, ProjectStoreFactory>();

        services.AddSingleton<IReconstructor, Reconstructor>();
        services.AddSingleton<IVisualReconstructor, VisualReconstructor>();

        services.AddSingleton<IModelAssetCache, ModelAssetCache>();
        services.AddSingleton<ISceneSegmenter, SceneSegmenter>();
        services.AddSingleton<ISemanticModelProvider, SemanticModelProvider>();

        services.AddSingleton<IEdgeCollapse, EdgeCollapse>();

        services.AddSingleton<IArtifactWriteObserver, ArtifactWriteObserver>();
        services.AddSingleton<IBinaryArtifactFiles, BinaryArtifactFiles>();
        services.AddSingleton<IBulkArtifactStore, BulkArtifactStore>();
        services.AddSingleton<IFeatureArtifactStore, FeatureArtifactStore>();
        services.AddSingleton<IMatchArtifactStore, MatchArtifactStore>();
        services.AddSingleton<IMatchingIndexStore, MatchingIndexStore>();
        services.AddSingleton<IProcessingResourcePlanner, ProcessingResourcePlanner>();

        services.AddSingleton<IPoissonSurfaceCodec, PoissonSurfaceCodec>();
        services.AddSingleton<ISurfaceReconstructor, PoissonSurfaceReconstructor>();

        services.AddSingleton<IPerformanceTraceExporter, PerformanceTraceExporter>();
        services.AddSingleton<IPerformanceTrace, PerformanceTraceService>();
        services.AddSingleton<IPerformanceTraceStore, PerformanceTraceStore>();
        services.AddSingleton<IProcessTelemetryReader, ProcessTelemetryReader>();

        services.AddSingleton<ITextureBaker, TextureBaker>();
        services.AddSingleton<ITextureChartOrienter, TextureChartOrienter>();
        services.AddSingleton<ITextureGapPacker, TextureGapPacker>();
        services.AddSingleton<IJsonReadMetadata, TextureJsonReadMetadata>();
        services.AddSingleton<ITextureLayoutPlanner, TextureLayoutPlanner>();
        services.AddSingleton<ITexturePatchBuilder, TexturePatchBuilder>();
        services.AddSingleton<ITexturePatchPacker, TexturePatchPacker>();
        services.AddSingleton<ITextureProfileBuilder, TextureProfileBuilder>();
        services.AddSingleton<ITextureProfilePacker, TextureProfilePacker>();
        services.AddSingleton<ITextureRasterizer, TextureRasterizer>();
        services.AddSingleton<ITextureSeamLeveler, TextureSeamLeveler>();
        services.AddSingleton<ITexturePlacementAccelerator, MetalTexturePlacementAccelerator>();

        services.AddSingleton<IPipelineStage, FeatureStage>();
        services.AddSingleton<IPipelineStage, MatchingStage>();
        services.AddSingleton<IPipelineStage, AlignmentStage>();
        services.AddSingleton<IPipelineStage, DepthStage>();
        services.AddSingleton<IPipelineStage, DenseCloudStage>();
        services.AddSingleton<IPipelineStage, FusionStage>();
        services.AddSingleton<IPipelineStage, MeshStage>();
        services.AddSingleton<IPipelineStage, TextureStage>();
        services.AddSingleton<IPipelineStage, ExportStage>();
    }
}
