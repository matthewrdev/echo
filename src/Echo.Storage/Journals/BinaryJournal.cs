using System.Text;

namespace Echo.Storage.Journals;

public sealed class BinaryJournal<T> : IDisposable
{
    private readonly FileStream stream;
    private readonly IBinaryJournalCodec<T> codec;
    private readonly MemoryStream record = new();
    private readonly BinaryWriter output;
    private readonly BinaryWriter payload;

    public BinaryJournal(string path, IBinaryJournalCodec<T> codec, long? committedLength = null)
    {
        this.codec = codec;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        output = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        payload = new BinaryWriter(record, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (stream.Length < sizeof(uint))
            {
                if (committedLength > sizeof(uint))
                {
                    throw new InvalidDataException("Truncated binary journal.");
                }

                stream.SetLength(0);
                output.Write(codec.Magic);
                Flush();
            }
            else
            {
                using var input = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                BinaryJournalFrame.ReadHeader(input, codec.Magic);
                if (committedLength is { } committed)
                {
                    if (committed < sizeof(uint) || committed > stream.Length)
                    {
                        throw new InvalidDataException("Invalid binary journal commit boundary.");
                    }

                    stream.Position = committed;
                }
                else
                {
                    var length = stream.Length;
                    while (BinaryJournalFrame.TryRead(input, length, out var size))
                    {
                        stream.Position += size;
                    }
                }

                if (stream.Position != stream.Length)
                {
                    stream.SetLength(stream.Position);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Append(T value, bool durable = true)
    {
        record.SetLength(0);
        codec.Write(payload, value);
        var size = checked((int)record.Length);
        BinaryJournalFrame.ValidateSize(size);
        output.Write(size);
        output.Write(record.GetBuffer(), 0, size);
        if (durable)
        {
            Flush();
        }
    }

    public long Length => stream.Position;

    public void Flush(bool durable = true) => stream.Flush(durable);

    public void CopyTo(string path)
    {
        Flush();
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.CopyTo(destination);
            destination.Flush(true);
        }
        finally
        {
            stream.Position = position;
        }
    }

    public void Dispose()
    {
        payload.Dispose();
        record.Dispose();
        output.Dispose();
        stream.Dispose();
    }
}
