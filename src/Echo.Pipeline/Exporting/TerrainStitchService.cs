using System.Text.Json;
using Echo.Export;
using Echo.Export.Terrain;
using Echo.Models;
using Echo.Projects;

namespace Echo.Pipeline.Exporting;

public sealed class TerrainStitchService : ITerrainStitchService
{
    private readonly IProjectStoreFactory projects;
    private readonly IRunExchangeService exchange;
    private readonly ITerrainStitchWriter writer;

    public TerrainStitchService(IProjectStoreFactory projects, IRunExchangeService exchange, ITerrainStitchWriter writer)
    {
        this.projects = projects;
        this.exchange = exchange;
        this.writer = writer;
    }

    public void Stitch(string manifestFile, string output, CancellationToken cancellation)
    {
        var manifestPath = Path.GetFullPath(manifestFile);
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<TerrainStitchManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new EchoException("Provide a terrain stitch manifest.", 2);
        Validate(manifest);
        var root = Path.GetDirectoryName(manifestPath)!;
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Terrain stitching needs a new output directory.", 2);
        }

        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var staged = Path.Combine(parent, $".echo-terrain-{Guid.NewGuid():N}");
        var work = Path.Combine(staged, "work");
        var result = Path.Combine(staged, "output");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(result);
        try
        {
            var inputs = new List<TerrainWallInput>();
            var walls = manifest.Walls ?? [];
            for (var index = 0; index < walls.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var wall = walls[index];
                var source = projects.Open(Path.GetFullPath(wall.Project, root));
                var package = Path.Combine(work, $"wall-{index + 1}");
                exchange.ExportMesh(source, wall.Run, package, MeshExportFormat.Glb);
                var name = string.IsNullOrWhiteSpace(wall.Name) ? source.Load().Name : wall.Name;
                inputs.Add(new TerrainWallInput(package, wall.HeightOffsetMetres, name));
            }

            var layers = new TerrainRasterLayers((manifest.PhotoOverlays ?? []).Select(path => Path.GetFullPath(path, root)).ToArray(),
                manifest.Footprint == null ? null : Path.GetFullPath(manifest.Footprint, root), manifest.Attribution);
            writer.Write(Path.GetFullPath(manifest.Elevation, root), Path.GetFullPath(manifest.Imagery, root), manifest.VerticalDatum, inputs, layers,
                result, work, manifest.GridLongEdge, manifest.TerrainTextureSize, manifest.MaxSeamErrorMetres, cancellation);
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(result, destination);
        }
        finally
        {
            Directory.Delete(staged, recursive: true);
        }
    }

    private static void Validate(TerrainStitchManifest manifest)
    {
        var hasSources = !string.IsNullOrWhiteSpace(manifest.Elevation) && !string.IsNullOrWhiteSpace(manifest.Imagery)
            && !string.IsNullOrWhiteSpace(manifest.VerticalDatum);
        var validSettings = manifest.GridLongEdge is >= 8 and <= 2048 && manifest.TerrainTextureSize is >= 64 and <= 8192
            && double.IsFinite(manifest.MaxSeamErrorMetres) && manifest.MaxSeamErrorMetres > 0;
        var validWalls = (manifest.Walls ?? []).All(wall => wall != null
            && !string.IsNullOrWhiteSpace(wall.Project) && !string.IsNullOrWhiteSpace(wall.Run)
            && !wall.Run.Equals("latest", StringComparison.OrdinalIgnoreCase));
        var validLayers = !(manifest.PhotoOverlays ?? []).Any(string.IsNullOrWhiteSpace)
            && (manifest.Footprint == null || !string.IsNullOrWhiteSpace(manifest.Footprint));
        var validInputs = hasSources && validWalls && validLayers;
        if (!validInputs || !validSettings)
        {
            throw new EchoException("Specify elevation and imagery rasters, a vertical datum, valid terrain settings and valid paths or saved run IDs for any optional layers.", 2);
        }
    }
}
