using System.Text;
using Echo.Models;
using Echo.Geometry;

namespace Echo.Export;

public sealed class LasWriter : ILasWriter
{
    public LasWriter()
    {
    }

    public void Write(string path, GeoPosition origin, IEnumerable<GeographicCloudPoint> points)
    {
        if (!GeographicCoordinates.HasEllipsoidalHeight(origin))
        {
            throw new EchoException("LAS needs a known WGS84 ellipsoidal height for EPSG:4978 coordinates.", 2);
        }

        const string wkt = "GEOCCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Geocentric X\",OTHER],AXIS[\"Geocentric Y\",OTHER],AXIS[\"Geocentric Z\",NORTH],AUTHORITY[\"EPSG\",\"4978\"]]";
        WriteFile(path, points, CameraGeometry.LocalToEcefMatrix(origin), Encoding.ASCII.GetBytes(wkt + "\0"));
    }

    public void WriteLocal(string path, IEnumerable<GeographicCloudPoint> points)
    {
        WriteFile(path, points, null, []);
    }

    private void WriteFile(string path, IEnumerable<GeographicCloudPoint> points, double[]? transform, byte[] crs)
    {
        var geographic = transform != null;
        var headerSize = geographic ? 375 : 227;
        var pointOffset = geographic ? headerSize + 54 + crs.Length : headerSize;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[headerSize]);
        if (geographic)
        {
            writer.Write((ushort)0);
            FixedText(writer, "LASF_Projection", 16);
            writer.Write((ushort)2112);
            writer.Write((ushort)crs.Length);
            FixedText(writer, "OGC coordinate system WKT", 32);
            writer.Write(crs);
        }

        double[] offset = transform == null ? [0, 0, 0] : [transform[3], transform[7], transform[11]];
        double[] minimum = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
        double[] maximum = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        ulong count = 0;
        foreach (var point in points)
        {
            var p = point.Position;
            var coordinates = transform == null
                ? new V3(p.X, -p.Z, p.Y)
                : new V3(
                    transform[0] * p.X + transform[1] * p.Y + transform[2] * p.Z,
                    transform[4] * p.X + transform[5] * p.Y + transform[6] * p.Z,
                    transform[8] * p.X + transform[9] * p.Y + transform[10] * p.Z);
            WriteCoordinate(writer, coordinates.X, 0, offset, minimum, maximum);
            WriteCoordinate(writer, coordinates.Y, 1, offset, minimum, maximum);
            WriteCoordinate(writer, coordinates.Z, 2, offset, minimum, maximum);
            writer.Write((ushort)0); // Intensity is not measured by photogrammetry.
            if (geographic)
            {
                writer.Write((byte)0x11); // One return, return number one.
                writer.Write((byte)0);
                writer.Write((byte)1); // Unclassified.
                writer.Write((byte)0);
                writer.Write((short)0);
                writer.Write((ushort)0);
                writer.Write(0d); // No GPS acquisition time.
            }
            else
            {
                writer.Write((byte)0x09); // One return, return number one in LAS 1.2.
                writer.Write((byte)1);
                writer.Write((sbyte)0);
                writer.Write((byte)0);
                writer.Write((ushort)0);
            }

            writer.Write((ushort)(point.Red * 257));
            writer.Write((ushort)(point.Green * 257));
            writer.Write((ushort)(point.Blue * 257));
            count++;
        }

        if (count == 0)
        {
            throw new EchoException("The point cloud contains no points.", 2);
        }

        stream.Position = 0;
        FixedText(writer, "LASF", 4);
        writer.Write((ushort)0);
        writer.Write((ushort)(geographic ? 16 : 0)); // WKT bit.
        writer.Write(new byte[16]);
        writer.Write((byte)1);
        writer.Write((byte)(geographic ? 4 : 2));
        FixedText(writer, "Echo photogrammetry", 32);
        FixedText(writer, "Echo", 32);
        writer.Write((ushort)DateTime.UtcNow.DayOfYear);
        writer.Write((ushort)DateTime.UtcNow.Year);
        writer.Write((ushort)headerSize);
        writer.Write((uint)pointOffset);
        writer.Write(geographic ? 1u : 0u);
        writer.Write((byte)(geographic ? 7 : 2));
        writer.Write((ushort)(geographic ? 36 : 26));
        var legacyCount = geographic ? 0u : checked((uint)count);
        writer.Write(legacyCount);
        writer.Write(legacyCount);
        writer.Write(new byte[16]);
        for (var axis = 0; axis < 3; axis++)
        {
            writer.Write(0.001d);
        }

        foreach (var value in offset)
        {
            writer.Write(value);
        }

        for (var axis = 0; axis < 3; axis++)
        {
            writer.Write(maximum[axis]);
            writer.Write(minimum[axis]);
        }

        if (geographic)
        {
            writer.Write(0UL); // No waveform data.
            writer.Write(0UL); // No EVLRs.
            writer.Write(0u);
            writer.Write(count);
            writer.Write(count);
            writer.Write(new byte[14 * 8]);
        }
    }

    private void WriteCoordinate(BinaryWriter writer, double delta, int axis, double[] offset, double[] minimum, double[] maximum)
    {
        var scaled = Math.Round(delta / 0.001);
        var invalid = !double.IsFinite(scaled) || scaled < int.MinValue || scaled > int.MaxValue;
        if (invalid)
        {
            throw new EchoException("LAS coordinate exceeds the supported millimetre precision range.", 2);
        }

        writer.Write((int)scaled);
        var value = scaled * 0.001 + offset[axis];
        minimum[axis] = Math.Min(minimum[axis], value);
        maximum[axis] = Math.Max(maximum[axis], value);
    }

    private void FixedText(BinaryWriter writer, string text, int length)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes(text.AsSpan(0, Math.Min(text.Length, length)), bytes);
        writer.Write(bytes);
    }
}
