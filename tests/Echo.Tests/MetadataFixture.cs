using System.Buffers.Binary;
using System.Text;

namespace Echo.Tests;

internal static class MetadataFixture
{
    internal static byte[] Tiff(bool littleEndian)
    {
        var bytes = new byte[277];
        bytes[0] = bytes[1] = (byte)(littleEndian ? 'I' : 'M');
        Write16(bytes, 2, 42, littleEndian);
        Write32(bytes, 4, 8, littleEndian);
        Write16(bytes, 8, 5, littleEndian);
        Entry(bytes, 10, 0x010f, 2, 4, 0, littleEndian);
        "DJI\0"u8.CopyTo(bytes.AsSpan(18));
        Entry(bytes, 22, 0x0110, 2, 7, 194, littleEndian);
        Entry(bytes, 34, 0x0112, 3, 1, 0, littleEndian);
        Write16(bytes, 42, 6, littleEndian);
        Entry(bytes, 46, 0x8769, 4, 1, 74, littleEndian);
        Entry(bytes, 58, 0x8825, 4, 1, 116, littleEndian);
        Write16(bytes, 74, 3, littleEndian);
        Entry(bytes, 76, 0x9003, 2, 20, 201, littleEndian);
        Entry(bytes, 88, 0x920a, 5, 1, 221, littleEndian);
        Entry(bytes, 100, 0xa405, 3, 1, 0, littleEndian);
        Write16(bytes, 108, 24, littleEndian);
        Write16(bytes, 116, 6, littleEndian);
        Entry(bytes, 118, 1, 2, 2, 0, littleEndian);
        bytes[126] = (byte)'S';
        Entry(bytes, 130, 2, 5, 3, 229, littleEndian);
        Entry(bytes, 142, 3, 2, 2, 0, littleEndian);
        bytes[150] = (byte)'W';
        Entry(bytes, 154, 4, 5, 3, 253, littleEndian);
        Entry(bytes, 166, 5, 1, 1, 0, littleEndian);
        bytes[174] = 1;
        Entry(bytes, 178, 6, 5, 1, 221, littleEndian);
        "FC8482\0"u8.CopyTo(bytes.AsSpan(194));
        "2026:03:07 07:11:43\0"u8.CopyTo(bytes.AsSpan(201));
        Rational(bytes, 221, 672, 100, littleEndian);
        Rational(bytes, 229, 35, 1, littleEndian);
        Rational(bytes, 237, 30, 1, littleEndian);
        Rational(bytes, 245, 0, 1, littleEndian);
        Rational(bytes, 253, 150, 1, littleEndian);
        Rational(bytes, 261, 45, 1, littleEndian);
        Rational(bytes, 269, 0, 1, littleEndian);
        return bytes;
    }

    internal static byte[] Jpeg()
    {
        using var stream = new MemoryStream();
        stream.Write([255, 216]);
        using var exif = new MemoryStream();
        exif.Write("Exif\0\0"u8);
        exif.Write(Tiff(true));
        Segment(stream, 0xe1, exif.ToArray());
        var xmp = "http://ns.adobe.com/xap/1.0/\0" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description xmlns:drone-dji=\"http://www.dji.com/drone-dji/1.0/\" drone-dji:AbsoluteAltitude=\"+87.202\" " +
            "drone-dji:GimbalPitchDegree=\"-13.40\" drone-dji:GimbalYawDegree=\"61.30\" drone-dji:GimbalRollDegree=\"0\" " +
            "drone-dji:CameraSerialNumber=\"001234\"/></rdf:RDF></x:xmpmeta>";
        Segment(stream, 0xe1, Encoding.UTF8.GetBytes(xmp));
        using var debug = new MemoryStream();
        debug.Write("DJI-DBG\0[hiso:"u8);
        debug.Write([0, 255, 93, 1, 2, 0]);
        debug.Write("][sensor_id:001234\0]"u8);
        Segment(stream, 0xe7, debug.ToArray());
        Segment(stream, 0xc0, [8, 0, 16, 0, 32, 1, 1, 0x11, 0]);
        stream.Write([255, 217]);
        return stream.ToArray();
    }

    private static void Segment(Stream stream, byte marker, byte[] data)
    {
        stream.Write([255, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
        stream.Write(data);
    }

    private static void Entry(byte[] bytes, int offset, ushort tag, ushort type, uint count, uint value, bool little)
    {
        Write16(bytes, offset, tag, little);
        Write16(bytes, offset + 2, type, little);
        Write32(bytes, offset + 4, count, little);
        Write32(bytes, offset + 8, value, little);
    }

    private static void Rational(byte[] bytes, int offset, uint numerator, uint denominator, bool little)
    {
        Write32(bytes, offset, numerator, little);
        Write32(bytes, offset + 4, denominator, little);
    }

    private static void Write16(byte[] bytes, int offset, ushort value, bool little)
    {
        if (little)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
        }
    }

    private static void Write32(byte[] bytes, int offset, uint value, bool little)
    {
        if (little)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        }
    }
}
