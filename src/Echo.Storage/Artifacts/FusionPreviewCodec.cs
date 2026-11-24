using Echo.Models;
using Echo.Storage.Journals;
using Echo.Storage.Records;

namespace Echo.Storage.Artifacts;

public sealed class FusionPreviewCodec
{
    private const uint Magic = 0x31504645; // EFP1
    private readonly VolumeRecordCodec samples = new();

    public void Write(BinaryWriter writer, FusionPreview preview)
    {
        var volume = preview.Volume;
        writer.Write(Magic);
        writer.Write(preview.TotalSamples);
        writer.Write(volume.VoxelSize);
        writer.Write(volume.InputSamples);
        writer.Write(volume.ConsistentSamples);
        writer.Write(volume.Samples.Count);
        var buffer = new byte[samples.Size * 4096];
        var count = 0;
        foreach (var sample in volume.Samples)
        {
            samples.Encode(sample, buffer.AsSpan(count * samples.Size, samples.Size));
            count++;
            if (count == 4096)
            {
                writer.Write(buffer);
                count = 0;
            }
        }

        writer.Write(buffer, 0, count * samples.Size);
    }

    public FusionPreview Read(BinaryReader reader)
    {
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported fusion preview format.");
        }

        var total = reader.ReadInt32();
        var volume = new FusedVolume
        {
            VoxelSize = reader.ReadDouble(),
            InputSamples = reader.ReadInt32(),
            ConsistentSamples = reader.ReadInt32()
        };
        var count = reader.ReadCount();
        var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remaining != (long)count * samples.Size)
        {
            throw new InvalidDataException("Invalid fusion preview size.");
        }

        var records = new VolumeSample[count];
        Span<byte> bytes = stackalloc byte[samples.Size];
        for (var index = 0; index < count; index++)
        {
            reader.BaseStream.ReadExactly(bytes);
            records[index] = samples.Decode(bytes);
        }

        volume.Samples = records;
        return new FusionPreview { TotalSamples = total, Volume = volume };
    }
}
