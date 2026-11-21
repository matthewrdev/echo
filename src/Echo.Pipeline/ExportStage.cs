using Echo.Models;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Texturing;

namespace Echo.Pipeline;

public sealed class ExportStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IGlbWriter glbWriter;

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
        var export = context.Build("export", new
        {
            descriptionVersion = 4,
            texture,
            meshPath,
            alignment,
            project.Settings.ExportValidate
        }, dir =>
                {
                    var tex = jsonCodec.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
                    var surface = context.ReadData<Surface>(store.PathFor(tex.MeshFile == null
                        ? meshPath + "/mesh.json" : texture + "/" + tex.MeshFile));
                    var uvs = context.ReadData<TextureCoordinates>(store.PathFor(texture + "/" + tex.UvsFile)).Values;
                    using var meshReport = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.PathFor(meshPath + "/mesh.report.json")));
                    var hasTopology = meshReport.RootElement.TryGetProperty("topology", out var topology);

                    glbWriter.Write(Path.Combine(dir, "cliff.glb"), surface, uvs, File.ReadAllBytes(store.PathFor(texture + "/atlas.jpg")));
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.geo.json"), new
                    {
                        schemaVersion = 1,
                        scene.Frame,
                        scene.Origin,
                        referenced = scene.Origin != null,
                        localToEcefRowMajor = scene.Origin == null ? null : CameraGeometry.LocalToEcefMatrix(scene.Origin),
                        axisConvention = "X east, Y up, Z south",
                        geographicDatum = "WGS84 horizontal",
                        verticalDatum = scene.Origin?.AltitudeDatum ?? "unreferenced",
                        formula = "Local tangent ECEF rotation around origin; stored model coordinates are East/Up/South. Unknown altitude datum precludes verified absolute elevation.",
                        cameras = scene.Cameras,
                        warnings = scene.Warnings
                    });
                    fileOperations.AtomicJson(Path.Combine(dir, "cliff.report.json"), new
                    {
                        schemaVersion = 2,
                        imageCount = images.Count,
                        cameras = scene.Cameras.Count,
                        points = scene.Points.Count,
                        scene.Bundle,
                        faces = surface.Faces.Count,
                        inferredColourFaces = surface.SourceImages.Count(string.IsNullOrEmpty),
                        closeHull = project.Settings.MeshCompleteSurface && project.Settings.MeshCloseHull,
                        topology = hasTopology ? (System.Text.Json.JsonElement?)topology : null,
                        textureSize = tex.Size,
                        tex.CoverageFraction,
                        scene.Unplaced,
                        warnings = scene.Warnings,
                        geometry = GeometryDescription(project.Settings.MeshCompleteSurface, project.Settings.MeshCloseHull),
                        alignment,
                        cloud = cloudPath,
                        fusion = fusionPath,
                        requestedTriangles = project.Settings.FaceLimit,
                        triangleBudgetIsCeiling = true,
                        targetReached = surface.Faces.Count <= project.Settings.FaceLimit
                    });
                    if (project.Settings.ExportValidate)
                    {
                        glbWriter.Inspect(Path.Combine(dir, "cliff.glb"));
                    }
                });
        context.Publish("export", export);
    }

    private string GeometryDescription(bool completeSurface, bool closeHull)
    {
        var completion = completeSurface
            ? closeHull
                ? "Screened Poisson completion retains the closed inferred hull, including unseen back and bottom surfaces. Explicit crop cuts remain open. Consult mesh topology for the measured closure result. "
                : "Screened Poisson gap completion is bounded to the observed neighbourhood; completed regions are inferred geometry. "
            : "Missing-surface inference is disabled; gaps in observed geometry remain open. ";
        return "Multiview-validated dense cloud, uncertainty-aware TSDF, consistent tetrahedral extraction, and adaptive simplification. "
            + completion
            + "Unseen regions retain geometry with smoothly interpolated boundary colours. Graph-cut photographic regions, exposure and seam correction, antialiased charts with padded gutters and mipmap filtering.";
    }
}
