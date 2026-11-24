using System.Buffers.Binary;
using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Storage.Artifacts;

public sealed class DenseCloudReportCodec
{
    private const uint Magic = 0x31444345; // ECD1
    private const int RecordBytes = 40;

    public DenseCloudReport Read(string path, bool includePreview = true)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        return Read(reader, includePreview);
    }

    public DenseCloudReport Read(BinaryReader reader, bool includePreview = true)
    {
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported dense cloud report.");
        }

        var report = new DenseCloudReport
        {
            InputSamples = reader.ReadInt32(),
            RetainedSamples = reader.ReadInt32()
        };
        var images = reader.ReadCount();
        for (var index = 0; index < images; index++)
        {
            report.ImageIds.Add(reader.ReadString());
        }

        var count = reader.ReadCount();
        if (reader.BaseStream.Length - reader.BaseStream.Position != (long)count * RecordBytes)
        {
            throw new InvalidDataException("Invalid dense cloud preview size.");
        }

        if (includePreview)
        {
            report.Preview.EnsureCapacity(count);
            for (var index = 0; index < count; index++)
            {
                var image = reader.ReadInt32();
                if (image < -1 || image >= images)
                {
                    throw new InvalidDataException("Dense cloud preview references an unknown image.");
                }

                var point = new CloudPoint
                {
                    ImageA = image < 0 ? "" : report.ImageIds[image],
                    Position = [reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()],
                    Error = reader.ReadDouble()
                };
                var hasColour = reader.ReadBoolean();
                var colour = reader.ReadBytes(3);
                point.Colour = hasColour ? colour : null;
                report.Preview.Add(point);
            }
        }

        return report;
    }

    public void Write(BinaryWriter writer, DenseCloudReport report)
    {
        writer.Write(Magic);
        writer.Write(report.InputSamples);
        writer.Write(report.RetainedSamples);
        writer.Write(report.ImageIds.Count);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < report.ImageIds.Count; index++)
        {
            var image = report.ImageIds[index];
            indices.TryAdd(image, index);
            writer.Write(image);
        }

        writer.Write(report.Preview.Count);
        var buffer = new byte[RecordBytes * 1024];
        var used = 0;
        foreach (var point in report.Preview)
        {
            if (point.Position.Length != 3 || point.Colour is { Length: not 3 })
            {
                throw new InvalidDataException("Invalid dense cloud preview point.");
            }

            var record = buffer.AsSpan(used, RecordBytes);
            BinaryPrimitives.WriteInt32LittleEndian(record, string.IsNullOrEmpty(point.ImageA) ? -1 : indices[point.ImageA]);
            BinaryPrimitives.WriteDoubleLittleEndian(record[4..], point.Position[0]);
            BinaryPrimitives.WriteDoubleLittleEndian(record[12..], point.Position[1]);
            BinaryPrimitives.WriteDoubleLittleEndian(record[20..], point.Position[2]);
            BinaryPrimitives.WriteDoubleLittleEndian(record[28..], point.Error);
            record[36..].Clear();
            if (point.Colour is { } colour)
            {
                record[36] = 1;
                colour.CopyTo(record[37..]);
            }

            used += RecordBytes;
            if (used == buffer.Length)
            {
                writer.Write(buffer);
                used = 0;
            }
        }

        writer.Write(buffer, 0, used);
    }
}
