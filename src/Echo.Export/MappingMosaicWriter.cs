using System.Globalization;
using System.Text.Json;
using Echo.Infrastructure;
using Echo.Models;

namespace Echo.Export;

public sealed class MappingMosaicWriter : IMappingMosaicWriter
{
    private readonly IMappingTools tools;
    private readonly IFileOperations files;

    public MappingMosaicWriter(IMappingTools tools, IFileOperations files)
    {
        this.tools = tools;
        this.files = files;
    }

    public void Write(IReadOnlyList<string> sources, string destination, double maximumSeamErrorMetres)
    {
        var tiles = sources.Select(ReadTile).ToArray();
        if (tiles.Length < 2 || tiles.Select(tile => tile.Directory).Distinct(StringComparer.Ordinal).Count() != tiles.Length)
        {
            throw new EchoException("Choose two or more distinct mapping exports to merge.", 2);
        }

        var reference = tiles[0];
        if (tiles.Any(tile => !CompatibleGrid(reference, tile)))
        {
            throw new EchoException("Mapping tiles must share a projected CRS, pixel size and grid alignment.", 2);
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new EchoException("Choose a new mosaic folder.", 2);
        }

        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            var seams = CheckSeams(tiles, staging, maximumSeamErrorMetres);
            foreach (var filename in new[] { "orthophoto.tif", "dsm.tif", "dtm.tif" })
            {
                MergeRaster(tiles, staging, filename);
            }

            MergeClouds(tiles, staging, reference.Crs);
            files.AtomicJson(Path.Combine(staging, "mosaic.json"), new
            {
                schemaVersion = 1,
                horizontalCrs = reference.Crs,
                verticalReference = "WGS84 ellipsoid",
                resolutionMetres = reference.Grid.Resolution,
                maximumSeamErrorMetres,
                tiles = tiles.Select(tile => tile.Directory).ToArray(),
                seams
            });
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private MappingTile ReadTile(string directory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var manifest = Path.Combine(path, "mapping.json");
        var products = new[] { "orthophoto.tif", "dsm.tif", "dtm.tif", "classified.laz" };
        if (!File.Exists(manifest) || products.Any(name => !File.Exists(Path.Combine(path, name))))
        {
            throw new EchoException($"Incomplete mapping export: {path}", 2);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = document.RootElement;
        if (root.GetProperty("verticalReference").GetString() != "WGS84 ellipsoid")
        {
            throw new EchoException($"Mosaics require a common absolute height datum. {path} lacks WGS84 ellipsoidal heights.", 2);
        }

        var data = root.GetProperty("grid");
        var grid = new MappingGrid(data.GetProperty("west").GetDouble(), data.GetProperty("south").GetDouble(),
            data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32(), data.GetProperty("resolution").GetDouble());
        return new MappingTile(path, root.GetProperty("horizontalCrs").GetString()!, grid);
    }

    private bool CompatibleGrid(MappingTile reference, MappingTile candidate)
    {
        var grid = reference.Grid;
        var other = candidate.Grid;
        if (reference.Crs != candidate.Crs || Math.Abs(grid.Resolution - other.Resolution) > 1e-9)
        {
            return false;
        }

        var dx = (grid.West - other.West) / grid.Resolution;
        var dy = (grid.South - other.South) / grid.Resolution;
        return Math.Abs(dx - Math.Round(dx)) < 1e-6 && Math.Abs(dy - Math.Round(dy)) < 1e-6;
    }

    private List<MappingSeam> CheckSeams(MappingTile[] tiles, string stage, double maximum)
    {
        var result = new List<MappingSeam>();
        var neighbours = Enumerable.Range(0, tiles.Length).Select(_ => new List<int>()).ToArray();
        for (var first = 0; first < tiles.Length; first++)
        {
            for (var second = first + 1; second < tiles.Length; second++)
            {
                var seam = MeasureSeam(tiles[first], tiles[second], stage);
                if (seam == null)
                {
                    continue;
                }

                if (seam.RootMeanSquareMetres > maximum)
                {
                    throw new EchoException(FormattableString.Invariant(
                        $"DSM seam RMSE {seam.RootMeanSquareMetres:F3} m exceeds {maximum:F3} m between {seam.First} and {seam.Second}."), 5);
                }

                result.Add(seam);
                neighbours[first].Add(second);
                neighbours[second].Add(first);
            }
        }

        var remaining = new Queue<int>();
        var reached = new HashSet<int> { 0 };
        remaining.Enqueue(0);
        while (remaining.TryDequeue(out var node))
        {
            foreach (var neighbour in neighbours[node])
            {
                if (reached.Add(neighbour))
                {
                    remaining.Enqueue(neighbour);
                }
            }
        }

        if (reached.Count != tiles.Length)
        {
            throw new EchoException("The tiles lack connected DSM overlap with valid elevations for seam checks.", 5);
        }

        return result;
    }

    private MappingSeam? MeasureSeam(MappingTile first, MappingTile second, string stage)
    {
        var a = first.Grid;
        var b = second.Grid;
        var horizontal = Math.Min(a.East, b.East) - Math.Max(a.West, b.West);
        var vertical = Math.Min(a.North, b.North) - Math.Max(a.South, b.South);
        if (horizontal < a.Resolution * .5 || vertical < a.Resolution * .5)
        {
            return null;
        }

        var path = Path.Combine(stage, "difference.tif");
        tools.Run("gdal_calc.py", "--quiet", "--overwrite", "--extent=intersect", "--type=Float32",
            "--NoDataValue=-9999", "--calc=abs(A-B)", "-A", first.PathFor("dsm.tif"),
            "-B", second.PathFor("dsm.tif"), "--outfile", path);
        using var data = JsonDocument.Parse(tools.Run("gdalinfo", "--config", "GDAL_PAM_ENABLED", "NO", "-stats", "-json", path));
        File.Delete(path);
        var band = data.RootElement.GetProperty("bands")[0];
        if (!band.TryGetProperty("metadata", out var metadata) || !metadata.TryGetProperty("", out var stats))
        {
            return null;
        }

        if (!stats.TryGetProperty("STATISTICS_MEAN", out var rawMean) || !stats.TryGetProperty("STATISTICS_STDDEV", out var rawDeviation))
        {
            return null;
        }

        var mean = double.Parse(rawMean.GetString()!, CultureInfo.InvariantCulture);
        var deviation = double.Parse(rawDeviation.GetString()!, CultureInfo.InvariantCulture);
        var valid = double.Parse(stats.GetProperty("STATISTICS_VALID_PERCENT").GetString()!, CultureInfo.InvariantCulture);
        var rmse = Math.Sqrt(mean * mean + deviation * deviation);
        return new MappingSeam(first.Directory, second.Directory, rmse, valid);
    }

    private void MergeRaster(MappingTile[] tiles, string directory, string name)
    {
        var vrt = Path.Combine(directory, name + ".vrt");
        var resolution = tiles[0].Grid.Resolution.ToString("R", CultureInfo.InvariantCulture);
        tools.Run("gdalbuildvrt", ["-strict", "-resolution", "user", "-tr", resolution, resolution, vrt,
            .. tiles.Select(tile => tile.PathFor(name))]);
        tools.Run("gdal_translate", "-q", "-of", "COG", "-co", "COMPRESS=DEFLATE", vrt, Path.Combine(directory, name));
        File.Delete(vrt);
    }

    private void MergeClouds(MappingTile[] tiles, string directory, string crs)
    {
        var stages = tiles.Select(tile => (object)new
        {
            type = "readers.las",
            filename = tile.PathFor("classified.laz")
        }).ToList();
        stages.Add(new
        {
            type = "filters.merge"
        });
        stages.Add(new
        {
            type = "writers.las",
            filename = Path.Combine(directory, "classified.laz"),
            a_srs = crs,
            compression = true,
            minor_version = 4,
            dataformat_id = 7,
            scale_x = 0.001,
            scale_y = 0.001,
            scale_z = 0.001,
            offset_x = "auto",
            offset_y = "auto",
            offset_z = "auto"
        });
        var path = Path.Combine(directory, "cloud-pipeline.json");
        File.WriteAllText(path, JsonSerializer.Serialize(stages));
        tools.Run("pdal", "pipeline", path);
        File.Delete(path);
    }
}
