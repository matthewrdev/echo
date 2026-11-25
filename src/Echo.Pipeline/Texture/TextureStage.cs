using Echo.Storage.Artifacts;
using Echo.Compute;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Texturing.Layout;
using Echo.Texturing;

namespace Echo.Pipeline.Texture;

public sealed class TextureStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ITextureBaker textureBaker;
    private readonly ITextureLayoutPlanner layoutPlanner;
    private readonly ITextureLayoutStore layouts;
    private readonly ITexturePlacementAccelerator[] packingAccelerators;

    public TextureStage(IFileOperations fileOperations, IJsonCodec jsonCodec, ITextureBaker textureBaker,
        ITextureLayoutPlanner layoutPlanner, ITextureLayoutStore layouts, IEnumerable<ITexturePlacementAccelerator> packingAccelerators)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.textureBaker = textureBaker;
        this.layoutPlanner = layoutPlanner;
        this.layouts = layouts;
        this.packingAccelerators = packingAccelerators.ToArray();
    }

    public string Name => "texture";
    public int Order => 7;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var progress = new TextureProgress(project.Settings, update => context.ReportOperation(Name, update));
        progress.Report(new OperationProgress("Load inputs", "Loading texture inputs…") { Steps = [] });
        var alignment = context.Artifact("alignment");
        project.Settings.TexturePriority?.ValidateAlignment(alignment);
        var scene = context.Scene();
        var meshPath = context.Artifact("mesh");
        var inputs = new Dictionary<string, object?>
        {
            ["meshPath"] = meshPath,
            ["alignment"] = alignment,
            ["textureSize"] = project.Settings.TextureSize,
            ["textureVersion"] = 19,
            ["texturePriority"] = project.Settings.TexturePriority,
            ["computeBackend"] = project.Settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Texture),
            ["packingImplementation"] = project.Settings.ComputeBackend == ComputeBackend.Cpu ? "cpu" : string.Join(";", packingAccelerators.Select(accelerator => accelerator.Implementation)),
            ["vegetationTextureScale"] = project.Settings.VegetationTextureScale,
            ["textureJpegQuality"] = project.Settings.TextureJpegQuality,
            ["textureChartFaces"] = project.Settings.TextureChartFaces,
            ["textureChartAngle"] = project.Settings.TextureChartAngle,
            ["textureGutter"] = project.Settings.TextureGutter,
            ["textureSuppressSky"] = project.Settings.TextureSuppressSky,
            ["textureSkyMarginPixels"] = project.Settings.TextureSkyMarginPixels
        };
        // Preserve the existing photographic checkpoint exactly when finishing is off.
        var originalPhotographicFinish = project.Settings.TextureColourEnhancement == 0 && project.Settings.TextureSeamBlendPixels == 0;
        object fingerprint = originalPhotographicFinish ? inputs : new Dictionary<string, object?>
        {
            ["inputs"] = inputs,
            ["colourFinishVersion"] = 2,
            ["seamSofteningVersion"] = 1,
            ["textureSeamBlendPixels"] = project.Settings.TextureSeamBlendPixels,
            ["textureColourEnhancement"] = project.Settings.TextureColourEnhancement
        };
        // Identity grades retain all existing checkpoints. A changed recipe only invalidates finishing.
        if (project.Settings.ColourGrade is { IsIdentity: false } grade)
        {
            grade.Validate();
            fingerprint = new Dictionary<string, object?>
            {
                ["photographic"] = fingerprint,
                ["colourGrade"] = grade
            };
        }

        string? layoutArtifact = null;
        var texture = context.Build("texture", fingerprint, dir =>
                {
                    var surface = context.ReadData<Surface>(store.PathFor(meshPath + "/mesh.json"));
                    TextureLayout? layout = null;
                    layoutArtifact = context.Build("texture-layout", new Dictionary<string, object?>
                    {
                        ["meshPath"] = meshPath,
                        ["alignment"] = alignment,
                        ["textureSize"] = project.Settings.TextureSize,
                        ["texturePriority"] = project.Settings.TexturePriority,
                        ["computeBackend"] = project.Settings.ComputeBackend,
                        ["computeImplementation"] = inputs["computeImplementation"],
                        ["packingImplementation"] = inputs["packingImplementation"],
                        ["optimiseVegetation"] = project.Settings.OptimiseVegetation,
                        ["vegetationTextureScale"] = project.Settings.VegetationTextureScale,
                        ["textureChartFaces"] = project.Settings.TextureChartFaces,
                        ["textureChartAngle"] = project.Settings.TextureChartAngle,
                        ["textureGutter"] = project.Settings.TextureGutter
                    }, directory =>
                    {
                        var vegetation = project.Settings.OptimiseVegetation
                            ? new FaceFlagsCodec().Read(store.PathFor(meshPath + "/vegetation.bin")) : null;
                        layout = layoutPlanner.Plan(project, scene, surface, cancellation,
                            message => context.ReportProgress(Name, message), vegetation, context.Work, progress.Report);
                        layouts.Write(Path.Combine(directory, "layout.bin"), layout, cancellation);
                    }, algorithmVersion: "echo-texture-layout-v2", sources: [meshPath, alignment]);
                    if (layout == null)
                    {
                        progress.Report(new OperationProgress("UV charts", "Loading saved UV charts…") { Steps = [] });
                        layout = layouts.Read(store.PathFor(layoutArtifact + "/layout.bin"), surface, cancellation);
                        progress.Report(new OperationProgress("Atlas packing", $"Reused {layout.Patches.Count:N0} packed UV charts.")
                        {
                            Steps = []
                        });
                    }
                    var result = textureBaker.Bake(store, project, scene, surface, Path.Combine(dir, "atlas.jpg"), cancellation,
                        message => context.ReportProgress(Name, message), operationProgress: progress.Report,
                        work: context.Work, preparedLayout: layout);
                    progress.Report(new OperationProgress("Save texture", "Saving textured geometry…", 0, 3, "files") { Steps = [] });
                    fileOperations.AtomicData(Path.Combine(dir, "mesh.json"), result.Surface!, cancellation);
                    progress.Report(new OperationProgress("Save texture", "Saving UV coordinates…", 1, 3, "files"));
                    result.UvsFile = "uvs.json";
                    fileOperations.AtomicData(Path.Combine(dir, result.UvsFile), new TextureCoordinates { Values = result.Uvs }, cancellation);
                    progress.Report(new OperationProgress("Save texture", "Saving texture metadata…", 2, 3, "files"));
                    fileOperations.AtomicJson(Path.Combine(dir, "texture.json"), result);
                    progress.Report(new OperationProgress("Save texture", "Texture outputs saved.", 3, 3, "files"));
                }, () => new ProcessingMetadata
                {
                    SourceArtifacts = [meshPath, alignment, layoutArtifact!]
                });
        context.Publish("texture", texture);
    }
}
