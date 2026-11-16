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
        var scene = context.Scene();
        var meshPath = context.Artifact("mesh");
        var texture = context.Build("texture", new TextureInputs
        {
            MeshPath = meshPath,
            Alignment = alignment,
            TextureSize = project.Settings.TextureSize,
            TextureVersion = 17,
            ComputeBackend = project.Settings.ComputeBackend,
            ComputeImplementation = context.ComputeImplementation(ComputeOperation.Texture),
            PackingImplementation = project.Settings.ComputeBackend == ComputeBackend.Cpu ? "cpu" : string.Join(";", packingAccelerators.Select(accelerator => accelerator.Implementation)),
            VegetationTextureScale = project.Settings.VegetationTextureScale,
            TextureJpegQuality = project.Settings.TextureJpegQuality,
            TextureChartFaces = project.Settings.TextureChartFaces,
            TextureChartAngle = project.Settings.TextureChartAngle,
            TextureGutter = project.Settings.TextureGutter,
            TextureSuppressSky = project.Settings.TextureSuppressSky,
            TextureSkyMarginPixels = project.Settings.TextureSkyMarginPixels
        }, dir =>
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
