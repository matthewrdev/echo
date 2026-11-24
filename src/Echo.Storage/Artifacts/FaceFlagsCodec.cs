using System.Collections;
using System.Text;

namespace Echo.Storage.Artifacts;

public sealed class FaceFlagsCodec
{
    private const uint Magic = 0x31464645; // EFF1

    public void Write(Stream stream, bool[] values)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(values.Length);
        var packed = new byte[PackedSize(values.Length)];
        new BitArray(values).CopyTo(packed, 0);
        writer.Write(packed);
    }

    public bool[] Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported face flags.");
        }

        var count = reader.ReadInt32();
        if (count < 0 || reader.BaseStream.Length - reader.BaseStream.Position != PackedSize(count))
        {
            throw new InvalidDataException("Invalid packed face flags size.");
        }

        var packed = reader.ReadBytes(PackedSize(count));
        var bits = new BitArray(packed) { Length = count };
        var values = new bool[count];
        bits.CopyTo(values, 0);
        return values;
    }

    private int PackedSize(int count) => (int)((count + 7L) / 8);
}
