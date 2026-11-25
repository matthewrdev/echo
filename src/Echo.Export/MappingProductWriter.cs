using System.Globalization;
using System.Text.Json;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Export;

public sealed class MappingProductWriter : IMappingProductWriter
{
    private readonly IMappingTools tools;

    public MappingProductWriter(IMappingTools tools)
    {
        this.tools = tools;
    }

    public void ValidateCrs(string crs)
    {
        using var definition = JsonDocument.Parse(tools.Run("gdalsrsinfo", "-o", "projjson", crs));
        var root = definition.RootElement;
        var projected = root.GetProperty("type").GetString() == "ProjectedCRS";
        if (!projected || root.GetProperty("coordinate_system").GetProperty("axis").EnumerateArray().Any(axis =>
                axis.GetProperty("unit").ValueKind != JsonValueKind.String || axis.GetProperty("unit").GetString() != "metre"))
        {
            throw new EchoException("Mapping requires a horizontal projected CRS with metre units.", 2);
        }
    }

    public MappingProducts Write(string localCloud, string modelRaster, string directory, GeoPosition origin,
        string crs, double resolution, bool relativeHeights)
    {
        var classified = Path.Combine(directory, "classified.laz");
        Classify(localCloud, classified, origin, crs, relativeHeights);
        var grid = ReadGrid(classified, resolution);
        var dsm = Path.Combine(directory, "dsm.tif");
        var dtm = Path.Combine(directory, "dtm.tif");
        var orthophoto = Path.Combine(directory, "orthophoto.tif");
        WriteElevation(classified, dsm, grid, groundOnly: false);
        WriteElevation(classified, dtm, grid, groundOnly: true);
        WriteOrthophoto(modelRaster, orthophoto, crs, grid);

        using var input = RasterInfo(modelRaster);
        var sourcePixel = input.RootElement.GetProperty("geoTransform")[1].GetDouble()
            * Math.Cos(origin.Latitude * Math.PI / 180);
        var alpha = Coverage(orthophoto, grid, alpha: true);
        var dsmCoverage = Coverage(dsm, grid, alpha: false);
        var dtmCoverage = Coverage(dtm, grid, alpha: false);
        if (alpha <= 0 || dsmCoverage <= 0)
        {
            throw new EchoException("Mapping produced an empty orthophoto or DSM. Check the cloud and textured mesh coverage.", 5);
        }

        return new MappingProducts(grid, sourcePixel, alpha, dsmCoverage, dtmCoverage);
    }

    private void Classify(string source, string destination, GeoPosition origin, string crs, bool relativeHeights)
    {
        // The local LAS uses east, north, up, while the scene uses east, up, south.
        var scene = CameraGeometry.LocalToEcefMatrix(origin);
        var transform = new[]
        {
            scene[0], -scene[2], scene[1], scene[3],
            scene[4], -scene[6], scene[5], scene[7],
            scene[8], -scene[10], scene[9], scene[11],
            0, 0, 0, 1
        };
        var pipeline = new List<object> { Reader(source) };
        if (relativeHeights)
        {
            pipeline.Add(Ferry("Z=>MappingHeight"));
        }

        pipeline.Add(new
        {
            type = "filters.transformation",
            matrix = string.Join(' ', transform.Select(Number)),
            override_srs = "EPSG:4978"
        });
        pipeline.Add(Reprojection("EPSG:4978", "EPSG:4979"));
        if (!relativeHeights)
        {
            pipeline.Add(Ferry("Z=>MappingHeight"));
        }

        pipeline.Add(Reprojection("EPSG:4979", crs));
        pipeline.Add(Ferry("MappingHeight=>Z"));
        pipeline.Add(new
        {
            type = "filters.smrf"
        });
        pipeline.Add(new
        {
            type = "writers.las",
            filename = destination,
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
        RunPipeline(destination, pipeline);
    }

    private MappingGrid ReadGrid(string path, double resolution)
    {
        using var info = JsonDocument.Parse(tools.Run("pdal", "info", "--summary", path));
        var bounds = info.RootElement.GetProperty("summary").GetProperty("bounds");
        return Cover(bounds.GetProperty("minx").GetDouble(), bounds.GetProperty("miny").GetDouble(),
            bounds.GetProperty("maxx").GetDouble(), bounds.GetProperty("maxy").GetDouble(), resolution);
    }

    private MappingGrid Cover(double minX, double minY, double maxX, double maxY, double resolution)
    {
        var firstColumn = Math.Floor(minX / resolution);
        var firstRow = Math.Floor(minY / resolution);
        var columns = Math.Floor(maxX / resolution) - firstColumn + 1;
        var rows = Math.Floor(maxY / resolution) - firstRow + 1;
        var validDimensions = double.IsFinite(columns) && double.IsFinite(rows) && columns >= 1 && rows >= 1;
        if (!validDimensions || columns * rows > 16_000_000)
        {
            throw new EchoException("The mapping grid exceeds 16 million cells or has invalid bounds. Choose a coarser resolution or crop the source run.", 2);
        }

        return new MappingGrid(firstColumn * resolution, firstRow * resolution, (int)columns, (int)rows, resolution);
    }

    private void WriteElevation(string source, string destination, MappingGrid grid, bool groundOnly)
    {
        var stages = new List<object> { Reader(source) };
        if (groundOnly)
        {
            stages.Add(new
            {
                type = "filters.expression",
                expression = "Classification == 2"
            });
        }

        stages.Add(new
        {
            type = "writers.gdal",
            filename = destination,
            resolution = grid.Resolution,
            origin_x = grid.West,
            origin_y = grid.South,
            width = grid.Width,
            height = grid.Height,
            radius = grid.Resolution * Math.Sqrt(2),
            output_type = groundOnly ? "idw" : "max",
            data_type = "float",
            nodata = -9999,
            gdalopts = "TILED=YES,COMPRESS=DEFLATE"
        });
        RunPipeline(destination, stages);
    }

    private void WriteOrthophoto(string source, string destination, string crs, MappingGrid grid)
    {
        tools.Run("gdalwarp", "-q", "-t_srs", crs, "-te", Number(grid.West), Number(grid.South),
            Number(grid.East), Number(grid.North), "-ts", grid.Width.ToString(CultureInfo.InvariantCulture),
            grid.Height.ToString(CultureInfo.InvariantCulture), "-r", "bilinear", "-dstalpha",
            "-co", "TILED=YES", "-co", "COMPRESS=DEFLATE", source, destination);
        RequireOutput(destination);
    }

    private JsonDocument RasterInfo(string path, bool statistics = false)
    {
        var arguments = new List<string> { "--config", "GDAL_PAM_ENABLED", "NO", "-json" };
        if (statistics)
        {
            arguments.Add("-stats");
        }

        arguments.Add(path);
        return JsonDocument.Parse(tools.Run("gdalinfo", arguments.ToArray()));
    }

    private double Coverage(string path, MappingGrid grid, bool alpha)
    {
        using var info = RasterInfo(path, statistics: true);
        var root = info.RootElement;
        var size = root.GetProperty("size");
        var transform = root.GetProperty("geoTransform");
        const double tolerance = 0.000001;
        var outputSizeMatches = size[0].GetInt32() == grid.Width && size[1].GetInt32() == grid.Height;
        var originMatches = Math.Abs(transform[0].GetDouble() - grid.West) < tolerance
            && Math.Abs(transform[3].GetDouble() - grid.North) < tolerance;
        var resolutionMatches = Math.Abs(transform[1].GetDouble() - grid.Resolution) < tolerance
            && Math.Abs(transform[5].GetDouble() + grid.Resolution) < tolerance;
        if (!outputSizeMatches || !originMatches || !resolutionMatches)
        {
            throw new EchoException($"{Path.GetFileName(path)} did not preserve the mapping grid.", 5);
        }

        var bands = root.GetProperty("bands");
        if (alpha)
        {
            foreach (var band in bands.EnumerateArray())
            {
                if (band.GetProperty("colorInterpretation").GetString() == "Alpha")
                {
                    return band.TryGetProperty("mean", out var mean) ? mean.GetDouble() * 100 / 255 : 0;
                }
            }

            throw new EchoException("The orthophoto is missing its alpha coverage band.", 5);
        }

        var hasStatistics = bands[0].TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("", out _);
        return hasStatistics
            && metadata.GetProperty("").TryGetProperty("STATISTICS_VALID_PERCENT", out var value)
            && double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var valid) ? valid : 0;
    }

    private void RunPipeline(string destination, IReadOnlyList<object> stages)
    {
        var path = destination + ".pipeline.json";
        File.WriteAllText(path, JsonSerializer.Serialize(stages));
        try
        {
            tools.Run("pdal", "pipeline", path);
            RequireOutput(destination);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void RequireOutput(string destination)
    {
        if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
        {
            throw new EchoException($"Mapping did not produce {Path.GetFileName(destination)}.", 5);
        }
    }

    private object Reader(string source) => new
    {
        type = "readers.las",
        filename = source
    };

    private object Ferry(string dimensions) => new
    {
        type = "filters.ferry",
        dimensions
    };

    private object Reprojection(string source, string target) => new
    {
        type = "filters.reprojection",
        in_srs = source,
        out_srs = target,
        error_on_failure = true
    };

    private string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
