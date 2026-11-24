using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Storage.Matching;

internal sealed class MatchingIndexRecordCodec : IBinaryJournalCodec<MatchingIndexEntry>
{
    public uint Magic => 0x314D4945; // EIM1

    public MatchingIndexEntry Read(BinaryReader reader) => new(
        reader.ReadInt32(), reader.ReadString(), reader.ReadString(), reader.ReadString(),
        reader.ReadInt32(), reader.ReadInt32());

    public void Write(BinaryWriter writer, MatchingIndexEntry value)
    {
        writer.Write(value.Sequence);
        writer.Write(value.Artifact);
        writer.Write(value.A);
        writer.Write(value.B);
        writer.Write(value.PointCount);
        writer.Write(value.LineCount);
    }
}
