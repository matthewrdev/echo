using System.ComponentModel.Composition;
using System.Security.Cryptography;
using Echo.Storage;

namespace Echo.Infrastructure;

[Export(typeof(IFileOperations)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FileOperations : IFileOperations
{
    private readonly IJsonCodec jsonCodec;
    private readonly IBulkArtifactStore bulk;

    [ImportingConstructor]
    public FileOperations(IJsonCodec jsonCodec, IBulkArtifactStore bulk)
    {
        this.jsonCodec = jsonCodec;
        this.bulk = bulk;
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
        jsonCodec.Write(stream, value);
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
        => AtomicWrite(path, stream => jsonCodec.Write(stream, value));

    public void AtomicBytes(string path, byte[] bytes) => AtomicWrite(path, stream => stream.Write(bytes));

    public void AtomicWrite(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }

            File.Move(temp, path, true);
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
