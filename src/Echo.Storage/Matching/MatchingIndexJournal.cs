using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Echo.Models;

namespace Echo.Storage.Matching;

public sealed class MatchingIndexJournal : IDisposable
{
    private readonly FileStream stream;
    private readonly List<MatchingIndexEntry> entries = [];
    private long lastFlush;

    public MatchingIndexJournal(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 65536);
        try
        {
            Recover();
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public int Count => entries.Count;

    public void Append(MatchingIndexEntry entry)
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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, MatchingStorageJsonContext.Default.MatchingIndexEntry);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        entries.Add(entry);
        // Make complete records visible to the viewer immediately. Durable index flushes
        // are grouped; immutable pair checkpoints remain recoverable after a power loss.
        stream.Flush();
        if (Stopwatch.GetTimestamp() - lastFlush >= Stopwatch.Frequency)
        {
            stream.Flush(true);
            lastFlush = Stopwatch.GetTimestamp();
        }
    }

    public void CopyTo(string path)
    {
        stream.Flush(true);
        var position = stream.Position;
        stream.Position = 0;
        try
        {
            using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536);
            stream.CopyTo(destination);
            destination.Flush(true);
        }
        finally
        {
            stream.Position = position;
        }
    }

    private void Recover()
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 65536, leaveOpen: true);
        long committed = 0;
        while (reader.ReadLine() is { } line)
        {
            var bytes = Encoding.UTF8.GetByteCount(line) + 1L;
            if (committed + bytes > stream.Length)
            {
                break;
            }
            var entry = JsonSerializer.Deserialize(line, MatchingStorageJsonContext.Default.MatchingIndexEntry)
                ?? throw new InvalidDataException("Invalid matching journal record.");
            if (entry.Sequence != entries.Count)
            {
                throw new InvalidDataException("Invalid matching journal sequence.");
            }
            entries.Add(entry);
            committed += bytes;
        }
        stream.SetLength(committed);
        stream.Position = committed;
    }

    public void Dispose()
    {
        stream.Flush(true);
        stream.Dispose();
    }
}
