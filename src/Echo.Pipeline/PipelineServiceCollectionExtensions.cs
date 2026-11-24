using Echo.Infrastructure;
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
using Microsoft.Extensions.DependencyInjection;

namespace Echo.Pipeline;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
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
        services.AddSingleton<IRunExchangeService, RunExchangeService>();
        services.AddSingleton<IRunStateRecovery, RunStateRecovery>();
        services.AddSingleton<ISceneSemanticArtifacts, SceneSemanticArtifacts>();
        services.AddSingleton<IStageResetService, StageResetService>();
        services.AddSingleton<ITextureLayoutStore, TextureLayoutStore>();

        services.AddSingleton<IPipelineStage, FeatureStage>();
        services.AddSingleton<IPipelineStage, MatchingStage>();
        services.AddSingleton<IPipelineStage, AlignmentStage>();
        services.AddSingleton<IPipelineStage, DepthStage>();
        services.AddSingleton<IPipelineStage, DenseCloudStage>();
        services.AddSingleton<IPipelineStage, FusionStage>();
        services.AddSingleton<IPipelineStage, MeshStage>();
        services.AddSingleton<IPipelineStage, TextureStage>();
        services.AddSingleton<IPipelineStage, ExportStage>();

        return services;
    }
}
