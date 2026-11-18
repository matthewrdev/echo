using Echo.Contracts;

namespace Echo.Storage;

public interface IMatchingIndexStore
{
    MatchingIndexJournal Open(string path);
    IReadOnlyList<MatchingIndexEntry> Read(string path);
    MatchingIndexEntry Describe(string projectRoot, string artifact, int sequence);
}
