using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Texturing;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ExportStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IGlbWriter glbWriter;
    private readonly ICameraGeometry cameraGeometry;

    [ImportingConstructor]
    public ExportStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IGlbWriter glbWriter, ICameraGeometry cameraGeometry)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.glbWriter = glbWriter;
        this.cameraGeometry = cameraGeometry;
    }

    public string Name => "export";
    public int Order => 8;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var store = context.Store;
        var alignment = context.Artifact("alignment");
        var scene = context.Scene();
        var meshPath = context.Artifact("mesh");
        var surface = context.ReadArtifact<Surface>("mesh", "mesh.json");
        var texture = context.Artifact("texture");
        var cloudPath = context.Artifact("cloud");
        var fusionPath = context.Artifact("fusion");
        var export = context.Build("export", new
        {
            texture,
            meshPath,
            alignment,
            project.Settings.ExportValidate
        }, dir =>
                {
                    var tex = jsonCodec.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
                    if (tex.MeshFile != null)
                    {
                        surface = jsonCodec.Read<Surface>(store.PathFor(texture + "/" + tex.MeshFile));
                    }

                    glbWriter.Write(Path.Combine(dir, "cliff.glb"), surface, tex.Uvs, File.ReadAllBytes(store.PathFor(texture + "/atlas.jpg")));
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.geo.json"), new
                    {
                        schemaVersion = 1,
                        scene.Frame,
                        scene.Origin,
                        referenced = scene.Origin != null,
                        localToEcefRowMajor = scene.Origin == null ? null : cameraGeometry.LocalToEcefMatrix(scene.Origin),
                        axisConvention = "X east, Y up, Z south",
                        geographicDatum = "WGS84 horizontal",
                        verticalDatum = scene.Origin?.AltitudeDatum ?? "unreferenced",
                        formula = "Local tangent ECEF rotation around origin; stored model coordinates are East/Up/South. Unknown altitude datum precludes verified absolute elevation.",
                        cameras = scene.Cameras,
                        warnings = scene.Warnings
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.report.json"), new
                    {
                        schemaVersion = 1,
                        imageCount = images.Count,
                        cameras = scene.Cameras.Count,
                        points = scene.Points.Count,
                        scene.Bundle,
                        faces = surface.Faces.Count,
                        textureSize = tex.Size,
                        tex.CoverageFraction,
                        scene.Unplaced,
                        warnings = scene.Warnings,
                        geometry = "Multiview-validated dense cloud, TSDF fusion and surface nets; unknown regions remain open. Shared photographic texture charts.",
                        alignment,
                        cloud = cloudPath,
                        fusion = fusionPath,
                        requestedTriangles = project.Settings.FaceLimit,
                        targetReached = surface.Faces.Count >= project.Settings.FaceLimit * .8
                    });
                    if (project.Settings.ExportValidate)
                    {
                        glbWriter.Inspect(Path.Combine(dir, "cliff.glb"));
                    }
                });
        context.Publish("export", export);
    }
}
