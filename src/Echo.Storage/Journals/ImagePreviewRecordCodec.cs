using Echo.Models;

namespace Echo.Storage.Journals;

public sealed class ImagePreviewRecordCodec : IBinaryJournalCodec<ImagePreviewRecord>
{
    public uint Magic => 0x31554945; // EIU1

    public ImagePreviewRecord Read(BinaryReader reader) => new(reader.ReadString(), reader.ReadString());

    public void Write(BinaryWriter writer, ImagePreviewRecord value)
    {
        writer.Write(value.ImageId);
        writer.Write(value.Artifact);
    }

    public Dictionary<string, string> ReadUnits(string path)
    {
        var reader = new BinaryJournalReader<ImagePreviewRecord>(this);
        using var stream = reader.OpenRead(path);
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stream != null)
        {
            foreach (var record in reader.Read(stream))
            {
                units[record.ImageId] = record.Artifact;
            }
        }

        return units;
    }
}
