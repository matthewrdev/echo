using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Pipeline;

public sealed class AlignmentPreviewReader : IAlignmentPreviewReader
{
    private readonly IJsonCodec json;

    public AlignmentPreviewReader(IJsonCodec json)
    {
        this.json = json;
    }

    public AlignmentPreview Read(string path, bool summary)
    {
        using var input = new BinaryReader(File.OpenRead(path));
        if (input.ReadInt32() != 0x31505345)
        {
            throw new InvalidDataException("Invalid sparse preview format.");
        }
        var length = input.ReadInt32();
        if (length < 0 || length > input.BaseStream.Length - input.BaseStream.Position)
        {
            throw new InvalidDataException("Invalid sparse preview metadata length.");
        }
        var header = JsonSerializer.Deserialize<AlignmentPreviewHeader>(input.ReadBytes(length), json.Options)
            ?? throw new InvalidDataException("Missing sparse preview metadata.");
        var validRecords = input.ReadInt32() == 0x31504345 && input.ReadInt32() == 1 && input.ReadInt32() == 76;
        if (!validRecords)
        {
            throw new InvalidDataException("Invalid sparse preview point records.");
        }
        var count = input.ReadInt32();
        var validCount = count >= 0 && count <= (input.BaseStream.Length - input.BaseStream.Position) / 76
            && header.TotalPoints >= count;
        if (!validCount)
        {
            throw new InvalidDataException("Invalid sparse preview point count.");
        }
        header.Scene.Points = [];
        if (!summary)
        {
            var points = new List<CloudPoint>(count);
            var codec = new CloudPointRecordCodec(header.Images);
            Span<byte> record = stackalloc byte[76];
            for (var index = 0; index < count; index++)
            {
                input.BaseStream.ReadExactly(record);
                points.Add(codec.Decode(record));
            }
            header.Scene.Points = points;
        }
        return new AlignmentPreview { TotalPoints = header.TotalPoints, Scene = header.Scene };
    }
}
