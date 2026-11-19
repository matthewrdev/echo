using Echo.Compute;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Texturing;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ITextureBaker textureBaker;
    private readonly ITexturePlacementAccelerator[] packingAccelerators;

    [ImportingConstructor]
    public TextureStage(IFileOperations fileOperations, IJsonCodec jsonCodec, ITextureBaker textureBaker, [ImportMany] IEnumerable<ITexturePlacementAccelerator> packingAccelerators)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.textureBaker = textureBaker;
        this.packingAccelerators = packingAccelerators.ToArray();
    }

    public string Name => "texture";
    public int Order => 7;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        project.Settings.TexturePriority?.ValidateAlignment(alignment);
        var scene = context.Scene();
        var meshPath = context.Artifact("mesh");
        var inputs = new
        {
            meshPath,
            alignment,
            project.Settings.TextureSize,
            textureVersion = 18,
            project.Settings.TexturePriority,
            project.Settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Texture),
            packingImplementation = project.Settings.ComputeBackend == ComputeBackend.Cpu ? "cpu" : string.Join(";", packingAccelerators.Select(accelerator => accelerator.Implementation)),
            project.Settings.VegetationTextureScale,
            project.Settings.TextureJpegQuality,
            project.Settings.TextureChartFaces,
            project.Settings.TextureChartAngle,
            project.Settings.TextureGutter,
            project.Settings.TextureSuppressSky,
            project.Settings.TextureSkyMarginPixels
        };
        // Preserve the existing photographic checkpoint exactly when finishing is off.
        var originalPhotographicFinish = project.Settings.TextureColourEnhancement == 0 && project.Settings.TextureSeamBlendPixels == 0;
        object fingerprint = originalPhotographicFinish ? inputs : new
        {
            inputs,
            colourFinishVersion = 2,
            seamSofteningVersion = 1,
            project.Settings.TextureSeamBlendPixels,
            project.Settings.TextureColourEnhancement
        };
        // Identity grades retain all existing checkpoints. A changed recipe only invalidates finishing.
        if (project.Settings.ColourGrade is { IsIdentity: false } grade)
        {
            grade.Validate();
            fingerprint = new
            {
                photographic = fingerprint,
                colourGrade = grade
            };
        }
        var texture = context.Build("texture", fingerprint, dir =>
                {
                    var surface = context.ReadData<Surface>(store.PathFor(meshPath + "/mesh.json"));
                    var vegetation = project.Settings.OptimiseVegetation
                        ? jsonCodec.Read<bool[]>(store.PathFor(meshPath + "/vegetation.json")) : null;
                    var result = textureBaker.Bake(store, project, scene, surface, Path.Combine(dir, "atlas.jpg"), cancellation, message => context.ReportProgress(Name, message), vegetation, update => context.ReportOperation(Name, update), context.Work);
                    fileOperations.AtomicData(Path.Combine(dir, "mesh.json"), result.Surface!, cancellation);
                    result.UvsFile = "uvs.json";
                    fileOperations.AtomicData(Path.Combine(dir, result.UvsFile), new TextureCoordinates { Values = result.Uvs }, cancellation);
                    fileOperations.AtomicJson(Path.Combine(dir, "texture.json"), result);
                });
        context.Publish("texture", texture);
    }
}
