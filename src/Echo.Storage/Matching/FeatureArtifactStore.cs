using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Records;

namespace Echo.Storage.Matching;

public sealed class FeatureArtifactStore : IFeatureArtifactStore
{
    private readonly IBinaryArtifactFiles files;

    public FeatureArtifactStore(IBinaryArtifactFiles files)
    {
        this.files = files;
    }

    public FeatureArtifactHeader Header(string path)
    {
        using var stream = File.OpenRead(path);
        var header = JsonSerializer.Deserialize(stream, MatchingStorageJsonContext.Default.FeatureArtifactHeader);
        var valid = header != null && header.Storage == "echo-features-v1" && header.PointCount >= 0
            && header.LineCount >= 0 && header.DescriptorCount >= 0;
        return valid ? header! : throw new InvalidDataException("Unsupported feature artifact. Migrate this project to binary matching storage.");
    }

    public FeatureSet Read(string path, bool descriptors = true)
    {
        var header = Header(path);
        var tableBytes = checked(header.PointCount * 32L + header.LineCount * 48L);
        using var stream = files.OpenPayload(path, header.Payload, 1, checked(tableBytes + header.DescriptorCount * 4L));
        var values = descriptors ? new float[header.DescriptorCount] : [];
        if (descriptors)
        {
            stream.Position = 16 + tableBytes;
            stream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = MemoryMarshal.AsBytes(values.AsSpan());
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(index * 4)..]);
                }
            }

            stream.Position = 16;
        }

        var result = new FeatureSet { ImageId = header.ImageId, Features = new(header.PointCount), Lines = new(header.LineCount) };
        Span<byte> record = stackalloc byte[48];
        for (var index = 0; index < header.PointCount; index++)
        {
            stream.ReadExactly(record[..32]);
            var kind = BinaryPrimitives.ReadInt32LittleEndian(record[24..]);
            if ((uint)kind >= header.Kinds.Count)
            {
                throw new InvalidDataException("Invalid feature kind.");
            }

            result.Features.Add(new Feature
            {
                X = BinaryPrimitives.ReadDoubleLittleEndian(record),
                Y = BinaryPrimitives.ReadDoubleLittleEndian(record[8..]),
                Kind = header.Kinds[kind],
                DescriptorData = Descriptor(record[16..], values, header.DescriptorCount, descriptors)
            });
        }

        for (var index = 0; index < header.LineCount; index++)
        {
            stream.ReadExactly(record);
            result.Lines.Add(new LineFeature
            {
                Start = [BinaryPrimitives.ReadDoubleLittleEndian(record), BinaryPrimitives.ReadDoubleLittleEndian(record[8..])],
                End = [BinaryPrimitives.ReadDoubleLittleEndian(record[16..]), BinaryPrimitives.ReadDoubleLittleEndian(record[24..])],
                Strength = BinaryPrimitives.ReadDoubleLittleEndian(record[32..]),
                DescriptorData = Descriptor(record[40..], values, header.DescriptorCount, descriptors)
            });
        }

        return result;
    }

    public void Write(string path, FeatureSet features, CancellationToken cancellation = default)
    {
        var packed = FeatureDescriptorPacking.Pack(features);
        var header = new FeatureArtifactHeader
        {
            Storage = "echo-features-v1",
            ImageId = features.ImageId,
            PointCount = features.Features.Count,
            LineCount = features.Lines.Count,
            DescriptorCount = packed.Values.Length,
            Kinds = features.Features.Select(feature => feature.Kind).Distinct(StringComparer.Ordinal).ToList()
        };
        header.Payload = files.WritePayload(path, 1, stream =>
        {
            WritePoints(stream, features, packed, header, cancellation);
            WriteLines(stream, features, packed, cancellation);
            var codec = new FloatRecordCodec();
            var bytes = new byte[65536];
            for (var first = 0; first < packed.Values.Length; first += bytes.Length / 4)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(bytes.Length / 4, packed.Values.Length - first);
                codec.EncodeBlock(packed.Values.AsSpan(first, count), bytes);
                stream.Write(bytes.AsSpan(0, count * 4));
            }
        });
        files.WriteHeader(path, header, MatchingStorageJsonContext.Default.FeatureArtifactHeader);
    }

    private void WritePoints(Stream stream, FeatureSet features, PackedFeatureDescriptors packed, FeatureArtifactHeader header, CancellationToken cancellation)
    {
        Span<byte> row = stackalloc byte[32];
        for (var index = 0; index < features.Features.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var feature = features.Features[index];
            row.Clear();
            BinaryPrimitives.WriteDoubleLittleEndian(row, feature.X);
            BinaryPrimitives.WriteDoubleLittleEndian(row[8..], feature.Y);
            BinaryPrimitives.WriteInt32LittleEndian(row[16..], packed.PointOffsets[index]);
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], feature.DescriptorData.Length);
            BinaryPrimitives.WriteInt32LittleEndian(row[24..], header.Kinds.IndexOf(feature.Kind));
            stream.Write(row);
        }
    }

    private void WriteLines(Stream stream, FeatureSet features, PackedFeatureDescriptors packed, CancellationToken cancellation)
    {
        Span<byte> row = stackalloc byte[48];
        for (var index = 0; index < features.Lines.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = features.Lines[index];
            if (line.Start.Length != 2 || line.End.Length != 2)
            {
                throw new InvalidDataException("Line features require two-dimensional endpoints.");
            }

            BinaryPrimitives.WriteDoubleLittleEndian(row, line.Start[0]);
            BinaryPrimitives.WriteDoubleLittleEndian(row[8..], line.Start[1]);
            BinaryPrimitives.WriteDoubleLittleEndian(row[16..], line.End[0]);
            BinaryPrimitives.WriteDoubleLittleEndian(row[24..], line.End[1]);
            BinaryPrimitives.WriteDoubleLittleEndian(row[32..], line.Strength);
            BinaryPrimitives.WriteInt32LittleEndian(row[40..], packed.LineOffsets[index]);
            BinaryPrimitives.WriteInt32LittleEndian(row[44..], line.DescriptorData.Length);
            stream.Write(row);
        }
    }

    private ReadOnlyMemory<float> Descriptor(ReadOnlySpan<byte> row, float[] values, int count, bool included)
    {
        var offset = BinaryPrimitives.ReadInt32LittleEndian(row);
        var length = BinaryPrimitives.ReadInt32LittleEndian(row[4..]);
        var valid = offset >= 0 && length >= 0 && offset <= count && length <= count - offset;
        if (!valid)
        {
            throw new InvalidDataException("Invalid feature descriptor range.");
        }

        return included ? values.AsMemory(offset, length) : ReadOnlyMemory<float>.Empty;
    }
}
