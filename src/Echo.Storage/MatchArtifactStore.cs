using System.Buffers.Binary;
using System.Text.Json;
using Echo.Models;

namespace Echo.Storage;

public sealed class MatchArtifactStore : IMatchArtifactStore
{
    private readonly IBinaryArtifactFiles files;

    public MatchArtifactStore(IBinaryArtifactFiles files)
    {
        this.files = files;
    }

    public MatchArtifactHeader Header(string path)
    {
        using var stream = File.OpenRead(path);
        var header = JsonSerializer.Deserialize(stream, MatchingStorageJsonContext.Default.MatchArtifactHeader);
        var valid = header != null && header.Storage == "echo-matches-v1"
            && header.PointCount >= 0 && header.LineCount >= 0;
        return valid ? header! : throw new InvalidDataException("Unsupported pair artifact. Migrate this project to binary matching storage.");
    }

    public ImagePair Read(string path)
    {
        var header = Header(path);
        using var stream = files.OpenPayload(path, header.Payload, 2, checked(header.PointCount * 48L + header.LineCount * 80L));
        var pair = new ImagePair
        {
            A = header.A,
            B = header.B,
            Diagnostics = header.Diagnostics,
            Verification = header.Verification,
            Matches = new(header.PointCount),
            LineMatches = new(header.LineCount)
        };
        Span<byte> row = stackalloc byte[80];
        for (var index = 0; index < header.PointCount; index++)
        {
            stream.ReadExactly(row[..48]);
            var kind = BinaryPrimitives.ReadInt32LittleEndian(row[40..]);
            var flags = BinaryPrimitives.ReadInt32LittleEndian(row[44..]);
            var validFlags = (flags & ~31) == 0 && (flags & 3) != 3;
            if ((uint)kind >= header.Kinds.Count || !validFlags)
            {
                throw new InvalidDataException("Invalid match kind or flags.");
            }
            pair.Matches.Add(new Match
            {
                A = (flags & 8) == 0 ? [] : Pixel(row),
                B = (flags & 16) == 0 ? [] : Pixel(row[16..]),
                Score = BinaryPrimitives.ReadDoubleLittleEndian(row[32..]),
                Kind = header.Kinds[kind],
                Manual = (flags & 4) != 0,
                GeometricInlier = (flags & 3) == 0 ? null : (flags & 3) == 2
            });
        }
        for (var index = 0; index < header.LineCount; index++)
        {
            stream.ReadExactly(row);
            pair.LineMatches.Add(new LineMatch
            {
                IndexA = BinaryPrimitives.ReadInt32LittleEndian(row),
                IndexB = BinaryPrimitives.ReadInt32LittleEndian(row[4..]),
                StartA = Pixel(row[8..]),
                EndA = Pixel(row[24..]),
                StartB = Pixel(row[40..]),
                EndB = Pixel(row[56..]),
                Score = BinaryPrimitives.ReadDoubleLittleEndian(row[72..])
            });
        }
        return pair;
    }

    public void Write(string path, ImagePair pair, CancellationToken cancellation = default)
    {
        var header = new MatchArtifactHeader
        {
            Storage = "echo-matches-v1",
            A = pair.A,
            B = pair.B,
            Diagnostics = pair.Diagnostics,
            Verification = pair.Verification,
            PointCount = pair.Matches.Count,
            LineCount = pair.LineMatches.Count,
            Kinds = pair.Matches.Select(match => match.Kind).Distinct(StringComparer.Ordinal).ToList()
        };
        header.Payload = files.WritePayload(path, 2, stream =>
        {
            WritePoints(stream, pair, header, cancellation);
            WriteLines(stream, pair, cancellation);
        });
        files.WriteHeader(path, header, MatchingStorageJsonContext.Default.MatchArtifactHeader);
    }

    private void WritePoints(Stream stream, ImagePair pair, MatchArtifactHeader header, CancellationToken cancellation)
    {
        Span<byte> row = stackalloc byte[48];
        foreach (var match in pair.Matches)
        {
            cancellation.ThrowIfCancellationRequested();
            row.Clear();
            WritePixel(row, match.A, true);
            WritePixel(row[16..], match.B, true);
            BinaryPrimitives.WriteDoubleLittleEndian(row[32..], match.Score);
            BinaryPrimitives.WriteInt32LittleEndian(row[40..], header.Kinds.IndexOf(match.Kind));
            var flags = match.GeometricInlier.HasValue ? match.GeometricInlier.Value ? 2 : 1 : 0;
            flags |= match.Manual ? 4 : 0;
            flags |= match.A.Length == 2 ? 8 : 0;
            flags |= match.B.Length == 2 ? 16 : 0;
            BinaryPrimitives.WriteInt32LittleEndian(row[44..], flags);
            stream.Write(row);
        }
    }

    private void WriteLines(Stream stream, ImagePair pair, CancellationToken cancellation)
    {
        Span<byte> row = stackalloc byte[80];
        foreach (var line in pair.LineMatches)
        {
            cancellation.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(row, line.IndexA);
            BinaryPrimitives.WriteInt32LittleEndian(row[4..], line.IndexB);
            WritePixel(row[8..], line.StartA, false);
            WritePixel(row[24..], line.EndA, false);
            WritePixel(row[40..], line.StartB, false);
            WritePixel(row[56..], line.EndB, false);
            BinaryPrimitives.WriteDoubleLittleEndian(row[72..], line.Score);
            stream.Write(row);
        }
    }

    private double[] Pixel(ReadOnlySpan<byte> row)
        => [BinaryPrimitives.ReadDoubleLittleEndian(row), BinaryPrimitives.ReadDoubleLittleEndian(row[8..])];

    private void WritePixel(Span<byte> row, double[] pixel, bool allowEmpty)
    {
        if (pixel.Length == 0 && allowEmpty)
        {
            return;
        }
        if (pixel.Length != 2)
        {
            throw new InvalidDataException("Matches require two-dimensional coordinates.");
        }
        BinaryPrimitives.WriteDoubleLittleEndian(row, pixel[0]);
        BinaryPrimitives.WriteDoubleLittleEndian(row[8..], pixel[1]);
    }
}
