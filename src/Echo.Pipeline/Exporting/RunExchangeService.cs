using Echo.DenseCloud;
using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Texturing;

namespace Echo.Pipeline.Exporting;

public sealed class RunExchangeService : IRunExchangeService
{
    private readonly IJsonCodec json;
    private readonly IBulkArtifactStore bulk;
    private readonly ICloudBuilder clouds;
    private readonly IGlbWriter glb;
    private readonly IMeshExchangeWriter mesh;
    private readonly IFileOperations files;
    private readonly IPointCloudExchangeWriter cloud;
    private readonly ILasWriter las;
    private readonly ISurveyCloudWriter survey;
    private readonly IPrintMeshWriter print;

    public RunExchangeService(IJsonCodec json, IBulkArtifactStore bulk, ICloudBuilder clouds, IGlbWriter glb,
        IMeshExchangeWriter mesh, IFileOperations files, IPointCloudExchangeWriter cloud, ILasWriter las,
        ISurveyCloudWriter survey, IPrintMeshWriter print)
    {
        this.json = json;
        this.bulk = bulk;
        this.clouds = clouds;
        this.glb = glb;
        this.mesh = mesh;
        this.files = files;
        this.cloud = cloud;
        this.las = las;
        this.survey = survey;
        this.print = print;
    }

    public RunExchangeResult ExportMesh(IProjectStore store, string runId, string destination, MeshExportFormat format, double? sizeMillimetres = null)
    {
        if (sizeMillimetres.HasValue != (format == MeshExportFormat.ThreeMf))
        {
            var message = format == MeshExportFormat.ThreeMf
                ? "Specify --size-mm for 3MF print exports."
                : "--size-mm is available for 3MF print exports.";
            throw new EchoException(message, 2);
        }

        var run = store.GetRun(runId);
        return WritePackage(store, run, destination, directory =>
        {
            WriteMesh(store, run, directory, format, sizeMillimetres);
            if (format != MeshExportFormat.ThreeMf)
            {
                WriteGeoreference(store, run, directory);
            }
        });
    }

    public RunExchangeResult ExportPointCloud(IProjectStore store, string runId, string destination, PointCloudExportFormat format, string? crs = null)
    {
        crs = ReadCrs(format, crs);
        var run = store.GetRun(runId);
        return WritePackage(store, run, destination, directory => WritePointCloud(store, run, directory, format, crs));
    }

    public RunExchangeResult ExportImages(IProjectStore store, string runId, string destination)
    {
        var run = store.GetRun(runId);
        return WritePackage(store, run, destination, directory => WriteImages(store, run, directory));
    }

    private void WriteImages(IProjectStore store, RunData run, string directory)
    {
        var photos = run.Snapshot.Images.Where(photo => photo.Enabled).ToList();
        if (photos.Count == 0)
        {
            throw new EchoException("The saved run contains no enabled images.", 2);
        }

        var imageDirectory = Path.Combine(directory, "images");
        Directory.CreateDirectory(imageDirectory);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exported = new List<ExportedPhoto>(photos.Count);
        foreach (var photo in photos)
        {
            var originalName = Path.GetFileName(photo.Name);
            var extension = Path.GetExtension(originalName);
            var source = store.PathFor($"sources/{photo.Id}.original{extension.ToLowerInvariant()}");
            var original = File.Exists(source);
            if (!original)
            {
                source = store.PathFor(photo.Source);
                extension = Path.GetExtension(photo.Source);
            }

            if (!File.Exists(source))
            {
                throw new EchoException($"The saved image {originalName} is missing from the project.", 2);
            }

            var stem = Path.GetFileNameWithoutExtension(originalName);
            var name = stem + extension;
            for (var suffix = 2; !usedNames.Add(name); suffix++)
            {
                name = FormattableString.Invariant($"{stem}-{suffix}{extension}");
            }

            File.Copy(source, Path.Combine(imageDirectory, name));
            exported.Add(new ExportedPhoto(photo.Id, originalName, "images/" + name, original));
        }

        files.AtomicJson(Path.Combine(directory, "photos.json"), new
        {
            runId = run.Id,
            photos = exported
        });
    }

    private string? ReadCrs(PointCloudExportFormat format, string? crs)
    {
        if (crs == null)
        {
            return format == PointCloudExportFormat.GeoLas ? "EPSG:4978" : null;
        }

        if (format is not (PointCloudExportFormat.Las or PointCloudExportFormat.GeoLas or PointCloudExportFormat.Laz or PointCloudExportFormat.Copc))
        {
            throw new EchoException("A target CRS is available for LAS, LAZ and COPC exports.", 2);
        }

        var code = crs.Trim().ToUpperInvariant();
        var valid = code.StartsWith("EPSG:", StringComparison.Ordinal)
            && int.TryParse(code.AsSpan(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var epsg)
            && epsg > 0;
        if (!valid)
        {
            throw new EchoException("Enter an EPSG code, such as EPSG:7856.", 2);
        }

        return code;
    }

    private void WriteMesh(IProjectStore store, RunData run, string directory, MeshExportFormat format, double? sizeMillimetres)
    {
        if (format == MeshExportFormat.Ply)
        {
            File.Copy(Artifact(store, run, "mesh", "mesh.ply", "Mesh"), Path.Combine(directory, "cliff.ply"));
            return;
        }

        var savedGlb = run.Artifacts.TryGetValue("export", out var export)
            ? store.PathFor(export + "/cliff.glb")
            : null;
        if (!File.Exists(savedGlb))
        {
            savedGlb = null;
        }

        if (format is MeshExportFormat.Glb or MeshExportFormat.Gltf)
        {
            WriteFromGlb(store, run, directory, format, savedGlb);
            return;
        }

        using var session = OpenSession();
        if (format is MeshExportFormat.Stl or MeshExportFormat.ThreeMf)
        {
            var meshPath = Artifact(store, run, "mesh", "mesh.json", "Mesh");
            var surface = session.Read<Surface>(meshPath);
            if (surface.Faces.Count == 0)
            {
                throw new EchoException("The saved mesh contains no triangles.", 2);
            }

            if (format == MeshExportFormat.ThreeMf)
            {
                print.Write(Path.Combine(directory, "cliff.3mf"), surface, sizeMillimetres!.Value);
            }
            else
            {
                mesh.WriteStl(Path.Combine(directory, "cliff.stl"), surface);
            }

            return;
        }

        var asset = ReadTexturedMesh(store, run, session);
        mesh.WriteTextured(directory, format, asset.Surface, asset.Uvs, asset.Atlas);
    }

    private void WriteFromGlb(IProjectStore store, RunData run, string directory, MeshExportFormat format, string? savedGlb)
    {
        var output = Path.Combine(directory, "cliff." + format.ToString().ToLowerInvariant());
        var temporary = savedGlb == null && format != MeshExportFormat.Glb;
        var source = savedGlb ?? (temporary ? Path.Combine(directory, "source.glb") : output);
        if (savedGlb == null)
        {
            using var session = OpenSession();
            var asset = ReadTexturedMesh(store, run, session);
            using var atlas = File.OpenRead(asset.Atlas);
            glb.Write(source, asset.Surface, asset.Uvs, atlas);
        }

        if (format == MeshExportFormat.Gltf)
        {
            mesh.WriteGltf(directory, source);
        }
        else if (savedGlb != null)
        {
            File.Copy(source, output);
        }

        if (temporary)
        {
            File.Delete(source);
        }
    }

    private void WriteGeoreference(IProjectStore store, RunData run, string directory)
    {
        var destination = Path.Combine(directory, "cliff.geo.json");
        if (run.Artifacts.TryGetValue("export", out var export))
        {
            var saved = store.PathFor(export + "/cliff.geo.json");
            if (File.Exists(saved))
            {
                File.Copy(saved, destination);
                return;
            }
        }

        if (run.Artifacts.TryGetValue("alignment", out var alignment))
        {
            var path = store.PathFor(alignment + "/scene.json");
            if (File.Exists(path))
            {
                var scene = json.Read<ReconstructionOverview>(path);
                files.AtomicJson(destination, GeoreferenceSidecar.Create(scene.Frame, scene.Origin, scene.Cameras, scene.Warnings));
            }
        }
    }

    private TexturedRunMesh ReadTexturedMesh(IProjectStore store, RunData run, BulkDataSession session)
    {
        var texturePath = Artifact(store, run, "texture", "texture.json", "Texture");
        var directory = Path.GetDirectoryName(texturePath)!;
        var texture = json.Read<TextureResult>(texturePath);
        var mesh = texture.MeshFile == null
            ? Artifact(store, run, "mesh", "mesh.json", "Mesh")
            : Path.Combine(directory, texture.MeshFile);
        var surface = session.Read<Surface>(mesh);
        var uvs = session.Read<TextureCoordinates>(Path.Combine(directory, texture.UvsFile)).Values;
        return new TexturedRunMesh(surface, uvs, Path.Combine(directory, "atlas.jpg"));
    }

    private void WritePointCloud(IProjectStore store, RunData run, string directory, PointCloudExportFormat format, string? crs)
    {
        var path = Artifact(store, run, "cloud", "cloud.ply", "Dense Cloud");
        var report = new DenseCloudReportCodec().Read(Path.Combine(Path.GetDirectoryName(path)!, "cloud.bin"), includePreview: false);
        if (report.RetainedSamples == 0)
        {
            throw new EchoException("The saved dense cloud contains no points.", 2);
        }

        if (format == PointCloudExportFormat.Ply)
        {
            File.Copy(path, Path.Combine(directory, "cloud.ply"));
            return;
        }

        using var session = OpenSession();
        var samples = clouds.Read(path, report, session).Samples;
        var points = samples.Select(sample =>
            new GeographicCloudPoint(new V3(sample.X, sample.Y, sample.Z), sample.Red, sample.Green, sample.Blue));
        if (format is PointCloudExportFormat.Las or PointCloudExportFormat.GeoLas or PointCloudExportFormat.Laz
            or PointCloudExportFormat.Copc or PointCloudExportFormat.E57)
        {
            WriteSurveyCloud(store, run, directory, format, crs, session, points);
            return;
        }

        cloud.Write(Path.Combine(directory, "cloud." + format.ToString().ToLowerInvariant()), format, samples.Count, points);
    }

    private void WriteSurveyCloud(IProjectStore store, RunData run, string directory, PointCloudExportFormat format,
        string? crs, BulkDataSession session, IEnumerable<GeographicCloudPoint> points)
    {
        GeoPosition? origin = null;
        if (crs != null)
        {
            var alignment = Artifact(store, run, "alignment", "scene.json", "Alignment");
            origin = session.Read<ReconstructionOverview>(alignment).Origin
                ?? throw new EchoException("A target CRS requires a geographically aligned run.", 2);
            if (!GeographicCoordinates.HasEllipsoidalHeight(origin))
            {
                throw new EchoException("A target CRS requires an explicit WGS84 ellipsoidal height for the run's origin.", 2);
            }
        }

        var projectedCrs = crs == "EPSG:4978" ? null : crs;
        var usePdal = projectedCrs != null || format is PointCloudExportFormat.Laz or PointCloudExportFormat.Copc or PointCloudExportFormat.E57;
        if (!usePdal)
        {
            WriteLas(Path.Combine(directory, "cloud.las"), origin, points);
            return;
        }

        survey.CheckAvailable(format, projectedCrs != null);
        var intermediate = Path.Combine(directory, "source.las");
        var filename = format switch
        {
            PointCloudExportFormat.Laz => "cloud.laz",
            PointCloudExportFormat.Copc => "cloud.copc.laz",
            PointCloudExportFormat.E57 => "cloud.e57",
            _ => "cloud.las"
        };
        WriteLas(intermediate, origin, points);
        survey.Write(intermediate, Path.Combine(directory, filename), format, projectedCrs);
        File.Delete(intermediate);
        if (projectedCrs != null)
        {
            files.AtomicJson(Path.Combine(directory, "cloud.crs.json"), new
            {
                horizontalCrs = projectedCrs,
                verticalDatum = "WGS84 ellipsoid",
                verticalUnit = "metre"
            });
        }
    }

    private void WriteLas(string path, GeoPosition? origin, IEnumerable<GeographicCloudPoint> points)
    {
        if (origin == null)
        {
            las.WriteLocal(path, points);
        }
        else
        {
            las.Write(path, origin, points);
        }
    }

    private BulkDataSession OpenSession() => bulk.OpenSession(64L * 1024 * 1024, 256L * 1024 * 1024);

    private string Artifact(IProjectStore store, RunData run, string stage, string filename, string title)
    {
        if (!run.Artifacts.TryGetValue(stage, out var folder))
        {
            throw new EchoException($"Process the selected run through {title} before exporting this format.", 2);
        }

        var path = store.PathFor(folder + "/" + filename);
        if (!File.Exists(path))
        {
            throw new EchoException($"The saved {title} artifact is missing {filename}.", 2);
        }

        return path;
    }

    private RunExchangeResult WritePackage(IProjectStore store, RunData run, string destination, Action<string> write)
    {
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Choose a new export folder.", 2);
        }

        foreach (var folder in new[] { "artifacts", "sources", "runs", "work" })
        {
            var managed = Path.GetFullPath(store.PathFor(folder)) + Path.DirectorySeparatorChar;
            if ((destination + Path.DirectorySeparatorChar).StartsWith(managed, StringComparison.OrdinalIgnoreCase))
            {
                throw new EchoException("Choose an export folder outside the project's managed folders.", 2);
            }
        }

        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            write(staging);
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(staging, path)).Order(StringComparer.Ordinal).ToArray();
            Directory.Move(staging, destination);
            return new RunExchangeResult(run.Id, destination, files);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
