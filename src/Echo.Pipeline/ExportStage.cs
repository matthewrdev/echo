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

    [ImportingConstructor]
    public ExportStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IGlbWriter glbWriter)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.glbWriter = glbWriter;
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
        var texture = context.Artifact("texture");
        var cloudPath = context.Artifact("cloud");
        var fusionPath = context.Artifact("fusion");
        var export = context.Build("export", new ExportInputs
        {
            Texture = texture,
            MeshPath = meshPath,
            Alignment = alignment,
            ExportValidate = project.Settings.ExportValidate
        }, dir =>
                {
                    var tex = jsonCodec.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
                    var surface = context.ReadData<Surface>(store.PathFor(tex.MeshFile == null
                        ? meshPath + "/mesh.json" : texture + "/" + tex.MeshFile));
                    var uvs = context.ReadData<TextureCoordinates>(store.PathFor(texture + "/" + tex.UvsFile)).Values;

                    glbWriter.Write(Path.Combine(dir, "cliff.glb"), surface, uvs, File.ReadAllBytes(store.PathFor(texture + "/atlas.jpg")));
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.geo.json"), new GeoreferenceReport
                    {
                        SchemaVersion = 1,
                        Frame = scene.Frame,
                        Origin = scene.Origin,
                        Referenced = scene.Origin != null,
                        LocalToEcefRowMajor = scene.Origin == null ? null : CameraGeometry.LocalToEcefMatrix(scene.Origin),
                        AxisConvention = "X east, Y up, Z south",
                        GeographicDatum = "WGS84 horizontal",
                        VerticalDatum = scene.Origin?.AltitudeDatum ?? "unreferenced",
                        Formula = "Local tangent ECEF rotation around origin; stored model coordinates are East/Up/South. Unknown altitude datum precludes verified absolute elevation.",
                        Cameras = scene.Cameras,
                        Warnings = scene.Warnings
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.report.json"), new ReconstructionReport
                    {
                        SchemaVersion = 2,
                        ImageCount = images.Count,
                        Cameras = scene.Cameras.Count,
                        Points = scene.Points.Count,
                        Bundle = scene.Bundle,
                        Faces = surface.Faces.Count,
                        InferredColourFaces = surface.SourceImages.Count(string.IsNullOrEmpty),
                        TextureSize = tex.Size,
                        CoverageFraction = tex.CoverageFraction,
                        Unplaced = scene.Unplaced,
                        Warnings = scene.Warnings,
                        Geometry = "Multiview-validated dense cloud, uncertainty-aware TSDF, consistent tetrahedral extraction, screened Poisson gap completion bounded to the observed neighbourhood, and adaptive simplification. Completed regions are inferred geometry; unseen regions retain geometry with smoothly interpolated boundary colours. Graph-cut photographic regions, exposure and seam correction, antialiased charts with padded gutters and mipmap filtering.",
                        Alignment = alignment,
                        Cloud = cloudPath,
                        Fusion = fusionPath,
                        RequestedTriangles = project.Settings.FaceLimit,
                        TriangleBudgetIsCeiling = true,
                        TargetReached = surface.Faces.Count <= project.Settings.FaceLimit
                    });
                    if (project.Settings.ExportValidate)
                    {
                        glbWriter.Inspect(Path.Combine(dir, "cliff.glb"));
                    }
                });
        context.Publish("export", export);
    }
}
