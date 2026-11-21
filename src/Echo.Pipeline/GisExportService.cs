using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Projects;
using Echo.Texturing;
using Echo.Storage;
using Echo.Imaging;
using SkiaSharp;

namespace Echo.Pipeline;

public sealed class GisExportService : IGisExportService
{
    private readonly IJsonCodec json;
    private readonly IGisWriter writer;
    private readonly IGeoPackageWriter packages;
    private readonly IRedpointGeoreferenceWriter redpoint;
    private readonly ITilesetWriter tilesets;
    private readonly ILasWriter las;
    private readonly ICloudBuilder clouds;
    private readonly IBulkArtifactStore bulk;
    private readonly IFileOperations files;

    public GisExportService(IJsonCodec json, IGisWriter writer, IGeoPackageWriter packages,
        IRedpointGeoreferenceWriter redpoint, ITilesetWriter tilesets, ILasWriter las,
        ICloudBuilder clouds, IBulkArtifactStore bulk, IFileOperations files)
    {
        this.json = json;
        this.writer = writer;
        this.packages = packages;
        this.redpoint = redpoint;
        this.tilesets = tilesets;
        this.las = las;
        this.clouds = clouds;
        this.bulk = bulk;
        this.files = files;
    }

    public GisExportResult Export(IProjectStore store, string runId, string destination)
    {
        var run = store.GetRun(runId);
        if (!run.Artifacts.TryGetValue("alignment", out var alignment))
        {
            throw new EchoException("A completed alignment is required for geographic export.", 2);
        }
        var scene = json.Read<ReconstructionOverview>(store.PathFor(alignment + "/scene.json")).ToScene();
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Choose a new export folder; existing files are never overwritten.", 2);
        }
        foreach (var protectedFolder in new[] { "artifacts", "sources", "runs", "work" })
        {
            var protectedPath = Path.GetFullPath(store.PathFor(protectedFolder)) + Path.DirectorySeparatorChar;
            if ((destination + Path.DirectorySeparatorChar).StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new EchoException("Export outside the project's managed artifact, source, run and work folders.", 2);
            }
        }
        var surface = ReadSurface(store, run);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var manifest = new GeographicExportManifest { RunId = run.Id, Warnings = [.. scene.Warnings] };
        try
        {
            writer.Write(staging, scene, surface, run.Snapshot.Images);
            packages.Write(Path.Combine(staging, "geography.gpkg"), scene, surface, run.Snapshot.Images);
            if (run.Artifacts.TryGetValue("export", out var export))
            {
                foreach (var file in new[] { "cliff.glb", "cliff.geo.json" })
                {
                    var source = store.PathFor(export + "/" + file);
                    if (File.Exists(source))
                    {
                        File.Copy(source, Path.Combine(staging, file));
                    }
                }
            }
            ExportModel(staging, scene, surface, manifest);
            ExportCloud(store, run, staging, scene.Origin!, manifest);
            manifest.Files = Directory.GetFiles(staging).Select(path => Path.GetFileName(path)).Append("export-manifest.json").Order(StringComparer.Ordinal).ToList();
            files.AtomicJson(Path.Combine(staging, "export-manifest.json"), manifest);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        return new GisExportResult
        {
            Directory = destination,
            RunId = run.Id,
            Cameras = scene.Cameras.Count,
            Triangles = surface?.Faces.Count ?? 0,
            Files = manifest.Files,
            Warnings = manifest.Warnings
        };
    }

    private void ExportModel(string directory, ReconstructionScene scene, Surface? surface, GeographicExportManifest manifest)
    {
        if (surface == null || !File.Exists(Path.Combine(directory, "cliff.glb")))
        {
            manifest.Warnings.Add("Redpoint and 3D Tiles need a saved GLB. Complete the export stage first.");
            return;
        }
        try
        {
            redpoint.Write(directory, scene.Origin!, surface);
        }
        catch (EchoException error)
        {
            manifest.Warnings.Add("Redpoint omitted: " + error.Message);
        }
        if (GeographicCoordinates.HasEllipsoidalHeight(scene.Origin!))
        {
            tilesets.Write(directory, scene.Origin!, surface);
        }
        else
        {
            manifest.Warnings.Add("3D Tiles omitted: the origin needs an explicit WGS84 ellipsoidal height. No vertical datum conversion is assumed.");
        }
    }

    private void ExportCloud(IProjectStore store, RunData run, string directory, GeoPosition origin, GeographicExportManifest manifest)
    {
        if (!GeographicCoordinates.HasEllipsoidalHeight(origin))
        {
            manifest.Warnings.Add("LAS omitted: EPSG:4978 export requires an explicit WGS84 ellipsoidal height.");
            return;
        }
        using var session = bulk.OpenSession(64L * 1024 * 1024, 256L * 1024 * 1024);
        var grade = new ColourGradeTransform(run.Snapshot.Settings.ColourGrade);
        if (run.Snapshot.Settings.ColourGrade is { IsIdentity: false } recipe)
        {
            files.AtomicJson(Path.Combine(directory, "cloud-colour-grade.json"), recipe);
        }
        if (run.Artifacts.TryGetValue("cloud", out var cloudPath))
        {
            var report = json.Read<DenseCloudOverview>(store.PathFor(cloudPath + "/cloud.json")).ToReport();
            var cloud = clouds.Read(store.PathFor(cloudPath + "/cloud.ply"), report, session);
            if (cloud.Samples.Count > 0)
            {
                las.Write(Path.Combine(directory, "cloud.las"), origin, cloud.Samples.Select(point =>
                    GradedPoint(new V3(point.X, point.Y, point.Z), point.Red, point.Green, point.Blue, grade)));
                return;
            }
        }
        var scene = session.Read<ReconstructionScene>(store.PathFor(run.Artifacts["alignment"] + "/scene.json"));
        if (scene.Points.Count > 0)
        {
            las.Write(Path.Combine(directory, "sparse.las"), origin, scene.Points.Select(point =>
                GradedPoint(new V3(point.Position), point.Colour?[0] ?? 255, point.Colour?[1] ?? 255, point.Colour?[2] ?? 255, grade)));
        }
        else
        {
            manifest.Warnings.Add("LAS omitted: this run has no saved point cloud.");
        }
    }

    private GeographicCloudPoint GradedPoint(V3 position, byte red, byte green, byte blue, ColourGradeTransform grade)
    {
        var colour = grade.Apply(new SKColor(red, green, blue));
        return new GeographicCloudPoint(position, colour.Red, colour.Green, colour.Blue);
    }

    private Surface? ReadSurface(IProjectStore store, RunData run)
    {
        if (!run.Artifacts.TryGetValue("mesh", out var mesh))
        {
            return null;
        }
        var file = store.PathFor(mesh + "/mesh.json");
        if (run.Artifacts.TryGetValue("texture", out var texture))
        {
            var result = json.Read<TextureResult>(store.PathFor(texture + "/texture.json"));
            if (result.MeshFile != null)
            {
                file = store.PathFor(texture + "/" + result.MeshFile);
            }
        }
        return json.Read<Surface>(file);
    }
}
