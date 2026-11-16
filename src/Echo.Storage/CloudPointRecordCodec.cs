using System.Buffers.Binary;
using Echo.Contracts;

namespace Echo.Storage;

// Eight IEEE-754 doubles, two photograph indices and four flag/colour bytes.
// Empty observations remain empty; no coordinate quantisation is applied.
public sealed class CloudPointRecordCodec : RecordCodec<CloudPoint>
{
    private readonly string[] images;
    private readonly Dictionary<string, int> indices;

    public CloudPointRecordCodec(IEnumerable<string> images)
    {
        this.images = images.ToArray();
        indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < this.images.Length; index++)
        {
            indices.Add(this.images[index], index);
        }
    }

    public override int Size => 76;

    public override CloudPoint Decode(ReadOnlySpan<byte> bytes)
    {
        var a = BinaryPrimitives.ReadInt32LittleEndian(bytes[64..]);
        var b = BinaryPrimitives.ReadInt32LittleEndian(bytes[68..]);
        var flags = bytes[72];
        var invalid = (uint)a >= (uint)images.Length || (uint)b >= (uint)images.Length || (flags & ~15) != 0;
        if (invalid)
        {
            throw new InvalidDataException("Invalid sparse point record.");
        }
        return new CloudPoint
        {
            Position = (flags & 2) == 0 ? [] : ReadVector(bytes, 3),
            PixelA = (flags & 4) == 0 ? [] : ReadVector(bytes[24..], 2),
            PixelB = (flags & 8) == 0 ? [] : ReadVector(bytes[40..], 2),
            Error = BinaryPrimitives.ReadDoubleLittleEndian(bytes[56..]),
            ImageA = images[a],
            ImageB = images[b],
            Colour = (flags & 1) == 0 ? null : bytes.Slice(73, 3).ToArray()
        };
    }

    public override void Encode(CloudPoint value, Span<byte> bytes)
    {
        bytes.Clear();
        var flags = WriteVector(value.Position, bytes, 3) ? 2 : 0;
        flags |= WriteVector(value.PixelA, bytes[24..], 2) ? 4 : 0;
        flags |= WriteVector(value.PixelB, bytes[40..], 2) ? 8 : 0;
        BinaryPrimitives.WriteDoubleLittleEndian(bytes[56..], value.Error);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[64..], indices[value.ImageA]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[68..], indices[value.ImageB]);
        if (value.Colour != null)
        {
            if (value.Colour.Length != 3)
            {
                throw new InvalidDataException("Sparse point colours must contain three channels.");
            }
            flags |= 1;
            value.Colour.CopyTo(bytes[73..]);
        }
        bytes[72] = (byte)flags;
    }

    private double[] ReadVector(ReadOnlySpan<byte> bytes, int count)
    {
        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(index * 8)..]);
        }
        return values;
    }

    private bool WriteVector(double[] values, Span<byte> bytes, int count)
    {
        if (values.Length == 0)
        {
            return false;
        }
        if (values.Length != count)
        {
            throw new InvalidDataException("Invalid sparse point vector dimensions.");
        }
        for (var index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes[(index * 8)..], values[index]);
        }
        return true;
    }
}
