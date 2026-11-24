using System.Diagnostics;
using Echo.Storage.Journals;
using Echo.Models;

namespace Echo.Storage.Matching;

public sealed class MatchingIndexJournal : IDisposable
{
    private readonly BinaryJournal<MatchingIndexEntry> journal;
    private readonly List<MatchingIndexEntry> entries;
    private long lastFlush;

    internal MatchingIndexJournal(string path, List<MatchingIndexEntry> entries)
    {
        this.entries = entries;
        journal = new BinaryJournal<MatchingIndexEntry>(path, new MatchingIndexRecordCodec());
    }

    public int Count => entries.Count;

    public void Append(MatchingIndexEntry entry, bool publish = true)
    {
        if (entry.Sequence < entries.Count)
        {
            if (entries[entry.Sequence] != entry)
            {
                throw new InvalidDataException("Matching journal does not belong to these run inputs.");
            }

            return;
        }

        if (entry.Sequence != entries.Count)
        {
            throw new InvalidDataException("Matching journal entries must be published in order.");
        }

        journal.Append(entry, durable: false);
        entries.Add(entry);
        if (!publish)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var durable = now - lastFlush >= Stopwatch.Frequency;
        journal.Flush(durable);
        if (durable)
        {
            lastFlush = now;
        }
    }

    public void CopyTo(string path) => journal.CopyTo(path);

    public void Dispose()
    {
        try
        {
            journal.Flush();
        }
        finally
        {
            journal.Dispose();
        }
    }
}
