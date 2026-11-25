using Echo.Models;

namespace Echo.Storage.Journals;

public sealed class ArtifactProcessingRecordCodec : IBinaryJournalCodec<ArtifactProcessingRecord>
{
    public uint Magic => 0x31505245; // ERP1

    public ArtifactProcessingRecord Read(BinaryReader reader)
    {
        var artifact = reader.ReadString();
        var reference = new ArtifactProcessingReference
        {
            Stage = reader.ReadString(),
            MetadataPath = reader.ReadBoolean() ? reader.ReadString() : null,
            Reused = reader.ReadBoolean(),
            ProvenanceStatus = reader.ReadString()
        };

        return new ArtifactProcessingRecord(artifact, reference);
    }

    public void Write(BinaryWriter writer, ArtifactProcessingRecord value)
    {
        writer.Write(value.Artifact);
        writer.Write(value.Reference.Stage);
        writer.Write(value.Reference.MetadataPath != null);
        if (value.Reference.MetadataPath != null)
        {
            writer.Write(value.Reference.MetadataPath);
        }

        writer.Write(value.Reference.Reused);
        writer.Write(value.Reference.ProvenanceStatus);
    }

    public IEnumerable<ArtifactProcessingRecord> ReadRecords(string path)
    {
        var reader = new BinaryJournalReader<ArtifactProcessingRecord>(this);
        using var stream = reader.OpenRead(path);
        if (stream != null)
        {
            foreach (var record in reader.Read(stream))
            {
                yield return record;
            }
        }
    }
}
