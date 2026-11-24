using Echo.Storage.Journals;
using Echo.Models;

namespace Echo.Storage.Matching;

public sealed class MatchingIndexStore : IMatchingIndexStore
{
    private readonly IMatchArtifactStore matches;

    public MatchingIndexStore(IMatchArtifactStore matches)
    {
        this.matches = matches;
    }

    public MatchingIndexJournal Open(string path) => new(path, ReadRecords(path));

    public IReadOnlyList<MatchingIndexEntry> Read(string path) => ReadRecords(path);

    private List<MatchingIndexEntry> ReadRecords(string path)
    {
        var reader = new BinaryJournalReader<MatchingIndexEntry>(new MatchingIndexRecordCodec());
        using var stream = reader.OpenRead(path);
        var result = new List<MatchingIndexEntry>();
        if (stream != null)
        {
            foreach (var entry in reader.Read(stream))
            {
                if (entry.Sequence != result.Count)
                {
                    throw new InvalidDataException("Invalid matching index sequence.");
                }

                result.Add(entry);
            }
        }

        return result;
    }

    public MatchingIndexEntry Describe(string projectRoot, string artifact, int sequence)
    {
        var pair = matches.Header(Path.Combine(projectRoot, artifact, "pair.json"));
        return new MatchingIndexEntry(sequence, artifact, pair.A, pair.B, pair.PointCount, pair.LineCount);
    }
}
