using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Echo.Models;

namespace Echo.Export.Terrain;

internal sealed class TerrainRasterReader
{
    private const float Missing = -999999;
    private readonly IMappingTools tools;

    public TerrainRasterReader(IMappingTools tools)
    {
        this.tools = tools;
    }

    public TerrainGrid Read(string elevation, string imagery, string datum, int gridLongEdge, int textureSize, string work, TerrainRasterLayers layers)
    {
        using var sourceInfo = JsonDocument.Parse(tools.Run("gdalinfo", "-json", elevation));
        if (!sourceInfo.RootElement.TryGetProperty("wgs84Extent", out var extent))
        {
            throw new EchoException("The elevation raster needs a valid horizontal CRS.", 2);
        }

        var points = extent.GetProperty("coordinates")[0].EnumerateArray().ToArray();
        var west = points.Min(point => point[0].GetDouble());
        var east = points.Max(point => point[0].GetDouble());
        var south = points.Min(point => point[1].GetDouble());
        var north = points.Max(point => point[1].GetDouble());
        var width = (east - west) * Math.Cos((north + south) * Math.PI / 360);
        var height = north - south;
        if (width <= 0 || height <= 0 || east - west > .5 || height > .5 || Math.Abs(north) >= 85 || Math.Abs(south) >= 85)
        {
            throw new EchoException("Terrain stitching requires a local, nonpolar raster extent under half a degree on each axis.", 2);
        }

        var columns = Math.Max(8, (int)Math.Round(gridLongEdge * width / Math.Max(width, height)));
        var rows = Math.Max(8, (int)Math.Round(gridLongEdge * height / Math.Max(width, height)));
        var warp = Path.Combine(work, "elevation.tif");
        var raw = Path.Combine(work, "elevation.bin");
        tools.Run("gdalwarp", "-novshiftgrid", "-t_srs", "EPSG:4326", "-te", Number(west), Number(south), Number(east), Number(north),
            "-ts", Integer(columns), Integer(rows), "-r", "bilinear", "-ot", "Float32", "-dstnodata", Number(Missing), elevation, warp);
        tools.Run("gdal_translate", "-q", "-of", "ENVI", "-b", "1", warp, raw);
        var header = File.ReadAllLines(Path.ChangeExtension(raw, ".hdr"));
        if (!header.Any(line => line.Trim() == "byte order = 0") || !BitConverter.IsLittleEndian)
        {
            throw new EchoException("GDAL did not write little-endian floating-point elevations.", 2);
        }

        var bytes = File.ReadAllBytes(raw);
        if (bytes.Length != checked(columns * rows * sizeof(float)))
        {
            throw new EchoException("The elevation raster size does not agree with its grid.", 2);
        }

        var heights = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        var window = Trim(heights, columns, rows);
        var longitudeStep = (east - west) / columns;
        var latitudeStep = (north - south) / rows;
        east = west + (window.Right + 1) * longitudeStep;
        south = north - (window.Bottom + 1) * latitudeStep;
        west += window.Left * longitudeStep;
        north -= window.Top * latitudeStep;

        var cropped = new float[window.Width * window.Height];
        for (var row = 0; row < window.Height; row++)
        {
            Array.Copy(heights, (window.Top + row) * columns + window.Left, cropped, row * window.Width, window.Width);
        }

        if (cropped.Any(Invalid))
        {
            throw new EchoException("The cropped terrain has missing elevations. Supply a complete elevation raster.", 2);
        }

        var ortho = Path.Combine(work, "imagery.tif");
        var jpeg = Path.Combine(work, "imagery.jpg");
        tools.Run("gdalwarp", "-t_srs", "EPSG:4326", "-te", Number(west), Number(south), Number(east), Number(north),
            "-ts", Integer(textureSize), Integer(textureSize), "-r", "bilinear", imagery, ortho);
        foreach (var overlay in layers.PhotoOverlays)
        {
            using var info = JsonDocument.Parse(tools.Run("gdalinfo", "-json", overlay));
            var bands = info.RootElement.GetProperty("bands");
            var isRgba = bands.GetArrayLength() == 4 && bands[3].GetProperty("colorInterpretation").GetString() == "Alpha";
            if (!isRgba || !info.RootElement.TryGetProperty("wgs84Extent", out _))
            {
                throw new EchoException($"Photo overlay needs a georeferenced RGBA raster: {overlay}", 2);
            }

            tools.Run("gdalwarp", "-srcalpha", "-r", "bilinear", overlay, ortho);
        }

        tools.Run("gdal_translate", "-q", "-of", "JPEG", "-b", "1", "-b", "2", "-b", "3", ortho, jpeg);
        var origin = new GeoPosition
        {
            Latitude = (north + south) / 2,
            Longitude = (east + west) / 2,
            Altitude = cropped[cropped.Length / 2],
            AltitudeDatum = datum
        };
        byte[]? mask = null;
        if (layers.Footprint != null)
        {
            // Faces span the spaces between elevation samples. Rasterize at the face centres.
            var path = Path.Combine(work, "footprint.bin");
            var coverage = Path.Combine(work, "footprint.tif");
            tools.Run("gdal_rasterize", "-of", "GTiff", "-ot", "Byte", "-burn", "1", "-init", "0", "-a_srs", "EPSG:4326",
                "-te", Number(west + longitudeStep / 2), Number(south + latitudeStep / 2),
                Number(east - longitudeStep / 2), Number(north - latitudeStep / 2),
                "-ts", Integer(window.Width - 1), Integer(window.Height - 1), layers.Footprint, coverage);
            tools.Run("gdal_translate", "-q", "-of", "ENVI", coverage, path);
            mask = File.ReadAllBytes(path);
            if (mask.Length != (window.Width - 1) * (window.Height - 1) || !mask.Contains((byte)1))
            {
                throw new EchoException("The footprint contains no terrain cells at this grid resolution.", 2);
            }
        }

        return new TerrainGrid(window.Width, window.Height, west, north, longitudeStep, latitudeStep, cropped, jpeg, origin, mask);
    }

    private static TerrainGridWindow Trim(float[] heights, int columns, int rows)
    {
        var left = 0;
        var right = columns - 1;
        var top = 0;
        var bottom = rows - 1;
        while (right - left >= 7 && bottom - top >= 7)
        {
            double ScoreRow(int row) => Enumerable.Range(left, right - left + 1).Average(column => Invalid(heights[row * columns + column]) ? 1d : 0d);
            double ScoreColumn(int column) => Enumerable.Range(top, bottom - top + 1).Average(row => Invalid(heights[row * columns + column]) ? 1d : 0d);
            var edges = new[] { ScoreRow(top), ScoreRow(bottom), ScoreColumn(left), ScoreColumn(right) };
            var maximum = edges.Max();
            if (maximum == 0)
            {
                return new TerrainGridWindow(left, top, right, bottom);
            }

            switch (Array.IndexOf(edges, maximum))
            {
                case 0: top++; break;
                case 1: bottom--; break;
                case 2: left++; break;
                default: right--; break;
            }
        }

        throw new EchoException("The elevation raster has no usable rectangular terrain window.", 2);
    }

    private static bool Invalid(float value) => !float.IsFinite(value) || value == Missing;
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
}
