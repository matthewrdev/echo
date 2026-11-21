using System.Text;
using System.Text.Json;
using Echo.Models;

namespace Echo.Storage.Matching;

public sealed class MatchingIndexStore : IMatchingIndexStore
{
    private readonly IMatchArtifactStore matches;

    public MatchingIndexStore(IMatchArtifactStore matches)
    {
        this.matches = matches;
    }

    public MatchingIndexJournal Open(string path) => new(path);

    public IReadOnlyList<MatchingIndexEntry> Read(string path)
    {
        var result = new List<MatchingIndexEntry>();
        if (!File.Exists(path))
        {
            return result;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        long committed = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false);
        while (reader.ReadLine() is { } line)
        {
            committed += Encoding.UTF8.GetByteCount(line) + 1L;
            if (committed > length)
            {
                break;
            }
            var entry = JsonSerializer.Deserialize(line, MatchingStorageJsonContext.Default.MatchingIndexEntry)
                ?? throw new InvalidDataException("Missing matching index entry.");
            if (entry.Sequence != result.Count)
            {
                throw new InvalidDataException("Invalid matching index sequence.");
            }
            result.Add(entry);
        }
        return result;
    }

    public MatchingIndexEntry Describe(string projectRoot, string artifact, int sequence)
    {
        var pair = matches.Header(Path.Combine(projectRoot, artifact, "pair.json"));
        return new MatchingIndexEntry(sequence, artifact, pair.A, pair.B, pair.PointCount, pair.LineCount);
    }
}
