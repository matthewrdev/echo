using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Echo.Storage.Artifacts;

public sealed class BinaryArtifactFiles : IBinaryArtifactFiles
{
    private readonly IArtifactWriteObserver writes;

    public BinaryArtifactFiles(IArtifactWriteObserver writes)
    {
        this.writes = writes;
    }

    public string WritePayload(string path, int kind, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string checksum;
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            using (var hashing = new ChecksummedWriteStream(output))
            {
                Span<byte> header = stackalloc byte[16];
                header.Clear();
                BinaryPrimitives.WriteInt32LittleEndian(header, 0x31424d45);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], 1);
                BinaryPrimitives.WriteInt32LittleEndian(header[8..], kind);
                hashing.Write(header);
                write(hashing);
                output.Flush(true);
                checksum = hashing.CompleteChecksum()!;
            }
            var filename = Path.GetFileNameWithoutExtension(path) + "-" + checksum + ".bin";
            var target = Path.Combine(Path.GetDirectoryName(path)!, filename);
            File.Move(temporary, target, true);
            writes.Record(target, checksum);
            return filename;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void WriteHeader<T>(string path, T header, JsonTypeInfo<T> type)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string checksum;
            using (var output = File.Create(temporary))
            using (var hashing = new ChecksummedWriteStream(output))
            {
                JsonSerializer.Serialize(hashing, header, type);
                output.Flush(true);
                checksum = hashing.CompleteChecksum()!;
            }
            File.Move(temporary, path, true);
            writes.Record(path, checksum);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public FileStream OpenPayload(string path, string payload, int kind, long bytes)
    {
        var invalidName = string.IsNullOrWhiteSpace(payload) || Path.GetFileName(payload) != payload
            || payload is "." or "..";
        if (invalidName || bytes < 0)
        {
            throw new InvalidDataException("Invalid matching payload path or length.");
        }
        var stream = new FileStream(Path.Combine(Path.GetDirectoryName(path)!, payload), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess);
        try
        {
            Span<byte> header = stackalloc byte[16];
            stream.ReadExactly(header);
            var validHeader = BinaryPrimitives.ReadInt32LittleEndian(header) == 0x31424d45
                && BinaryPrimitives.ReadInt32LittleEndian(header[4..]) == 1
                && BinaryPrimitives.ReadInt32LittleEndian(header[8..]) == kind;
            if (!validHeader || stream.Length != checked(bytes + 16))
            {
                throw new InvalidDataException("Invalid matching binary header or length.");
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
