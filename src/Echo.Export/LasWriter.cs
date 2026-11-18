using System.ComponentModel.Composition;
using System.Text;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Export;

[Export(typeof(ILasWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class LasWriter : ILasWriter
{
    [ImportingConstructor]
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
        var crs = Encoding.ASCII.GetBytes(wkt + "\0");
        const int headerSize = 375;
        var pointOffset = headerSize + 54 + crs.Length;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[headerSize]);
        writer.Write((ushort)0);
        FixedText(writer, "LASF_Projection", 16);
        writer.Write((ushort)2112);
        writer.Write((ushort)crs.Length);
        FixedText(writer, "OGC coordinate system WKT", 32);
        writer.Write(crs);

        var m = CameraGeometry.LocalToEcefMatrix(origin);
        double[] offset = [m[3], m[7], m[11]];
        double[] minimum = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
        double[] maximum = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        ulong count = 0;
        foreach (var point in points)
        {
            WriteCoordinate(writer, m[0] * point.Position.X + m[1] * point.Position.Y + m[2] * point.Position.Z, 0, offset, minimum, maximum);
            WriteCoordinate(writer, m[4] * point.Position.X + m[5] * point.Position.Y + m[6] * point.Position.Z, 1, offset, minimum, maximum);
            WriteCoordinate(writer, m[8] * point.Position.X + m[9] * point.Position.Y + m[10] * point.Position.Z, 2, offset, minimum, maximum);
            writer.Write((ushort)0); // Intensity is not measured by photogrammetry.
            writer.Write((byte)0x11); // One return, return number one.
            writer.Write((byte)0);
            writer.Write((byte)1); // Unclassified.
            writer.Write((byte)0);
            writer.Write((short)0);
            writer.Write((ushort)0);
            writer.Write(0d); // No GPS acquisition time.
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
        writer.Write((ushort)16); // WKT bit.
        writer.Write(new byte[16]);
        writer.Write((byte)1);
        writer.Write((byte)4);
        FixedText(writer, "Echo photogrammetry", 32);
        FixedText(writer, "Echo", 32);
        writer.Write((ushort)DateTime.UtcNow.DayOfYear);
        writer.Write((ushort)DateTime.UtcNow.Year);
        writer.Write((ushort)headerSize);
        writer.Write((uint)pointOffset);
        writer.Write(1u); // One VLR.
        writer.Write((byte)7); // LAS 1.4 colour point format.
        writer.Write((ushort)36);
        writer.Write(new byte[24]); // Legacy counts must be zero for format 7.
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
        writer.Write(0UL); // No waveform data.
        writer.Write(0UL); // No EVLRs.
        writer.Write(0u);
        writer.Write(count);
        writer.Write(count);
        writer.Write(new byte[14 * 8]);
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
