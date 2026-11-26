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
            for (var index = 0; index < manifest.Walls.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var wall = manifest.Walls[index];
                var source = projects.Open(Path.GetFullPath(wall.Project, root));
                var package = Path.Combine(work, $"wall-{index + 1}");
                exchange.ExportMesh(source, wall.Run, package, MeshExportFormat.Glb);
                var name = string.IsNullOrWhiteSpace(wall.Name) ? source.Load().Name : wall.Name;
                inputs.Add(new TerrainWallInput(package, wall.HeightOffsetMetres, name));
            }

            writer.Write(Path.GetFullPath(manifest.Elevation, root), Path.GetFullPath(manifest.Imagery, root), manifest.VerticalDatum, inputs,
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
        var validWalls = manifest.Walls is { Count: > 0 } && manifest.Walls.All(wall => wall != null
            && !string.IsNullOrWhiteSpace(wall.Project) && !string.IsNullOrWhiteSpace(wall.Run)
            && !wall.Run.Equals("latest", StringComparison.OrdinalIgnoreCase));
        if (!hasSources || !validSettings || !validWalls)
        {
            throw new EchoException("Specify elevation and imagery rasters, a vertical datum, one or more saved wall run IDs, and valid terrain settings.", 2);
        }
    }
}
