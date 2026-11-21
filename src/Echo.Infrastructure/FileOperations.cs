using System.Security.Cryptography;
using System.Text.Json;
using Echo.Storage;
using Echo.Models;

namespace Echo.Infrastructure;

public sealed class FileOperations : IFileOperations
{
    private readonly IJsonCodec jsonCodec;
    private readonly IBulkArtifactStore bulk;
    private readonly IArtifactWriteObserver writes;

    public FileOperations(IJsonCodec jsonCodec, IBulkArtifactStore bulk, IArtifactWriteObserver writes)
    {
        this.jsonCodec = jsonCodec;
        this.bulk = bulk;
        this.writes = writes;
    }

    public string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public string Hash(object value)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        JsonSerializer.Serialize(stream, value, jsonCodec.Options);
        stream.FlushFinalBlock();
        return Convert.ToHexStringLower(hash.Hash!);
    }

    public void AtomicData<T>(string path, T value, CancellationToken cancellation = default)
    {
        if (bulk.CanStore(value))
        {
            bulk.Write(path, value, cancellation);
        }
        else
        {
            AtomicWrite(path, stream => jsonCodec.Write(stream, value));
        }
    }

    public void AtomicJson<T>(string path, T value)
    {
        if (value is FeatureSet or ImagePair)
        {
            bulk.Write(path, value);
            return;
        }
        AtomicWrite(path, stream => jsonCodec.Write(stream, value));
    }

    public void AtomicBytes(string path, byte[] bytes) => AtomicWrite(path, stream => stream.Write(bytes));

    public void AtomicWrite(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string? checksum;
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hashing = new ChecksummedWriteStream(stream))
            {
                write(hashing);
                stream.Flush(true);
                checksum = hashing.CompleteChecksum();
            }

            File.Move(temp, path, true);
            if (checksum != null)
            {
                writes.Record(path, checksum);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
