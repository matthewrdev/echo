using System.Security.Cryptography;

namespace Echo.Storage;

// A sequential write produces its checksum without a second read. Seekable
// callers remain supported, but their checksum must be obtained from the file.
public sealed class ChecksummedWriteStream : Stream
{
    private readonly Stream output;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool sequential = true;

    public ChecksummedWriteStream(Stream output)
    {
        this.output = output;
    }

    public string? CompleteChecksum() => sequential ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null;
    public override bool CanRead => false;
    public override bool CanSeek => output.CanSeek;
    public override bool CanWrite => output.CanWrite;
    public override long Length => output.Length;
    public override long Position
    {
        get => output.Position;
        set
        {
            sequential = false;
            output.Position = value;
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        output.Write(buffer);
        if (sequential)
        {
            hash.AppendData(buffer);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void WriteByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        Write(bytes);
    }

    public override void Flush() => output.Flush();
    public override long Seek(long offset, SeekOrigin origin)
    {
        sequential = false;
        return output.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        sequential = false;
        output.SetLength(value);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
