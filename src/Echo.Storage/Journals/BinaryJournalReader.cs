using System.Text;

namespace Echo.Storage.Journals;

public sealed class BinaryJournalReader<T>
{
    private readonly IBinaryJournalCodec<T> codec;

    public BinaryJournalReader(IBinaryJournalCodec<T> codec)
    {
        this.codec = codec;
    }

    public FileStream? OpenRead(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public IEnumerable<T> Read(FileStream stream, long? committedLength = null)
    {
        var length = committedLength ?? stream.Length;
        if (length > stream.Length)
        {
            throw new InvalidDataException("Truncated binary journal.");
        }

        if (length < sizeof(uint))
        {
            yield break;
        }

        using var input = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        BinaryJournalFrame.ReadHeader(input, codec.Magic);
        while (BinaryJournalFrame.TryRead(input, length, out var size))
        {
            var end = stream.Position + size;
            var value = codec.Read(input);
            if (stream.Position != end)
            {
                throw new InvalidDataException("Invalid binary journal record.");
            }

            yield return value;
        }

        if (committedLength.HasValue && stream.Position != length)
        {
            throw new InvalidDataException("Invalid binary journal commit boundary.");
        }
    }

    public T? ReadLast(FileStream stream)
    {
        var length = stream.Length;
        if (length < sizeof(uint))
        {
            return default;
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        BinaryJournalFrame.ReadHeader(reader, codec.Magic);
        var last = -1L;
        while (BinaryJournalFrame.TryRead(reader, length, out var size))
        {
            last = stream.Position;
            stream.Position += size;
        }

        if (last < 0)
        {
            return default;
        }

        stream.Position = last;
        return codec.Read(reader);
    }
}
