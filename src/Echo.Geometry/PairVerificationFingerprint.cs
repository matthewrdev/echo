using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Echo.Models;

namespace Echo.Geometry;

public static class PairVerificationFingerprint
{
    public static string Compute(ImagePair pair, double threshold, string implementation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"fundamental-1:{pair.A}:{pair.B}:{implementation}"));
        Span<byte> row = stackalloc byte[24];
        BinaryPrimitives.WriteDoubleLittleEndian(row, threshold);
        BinaryPrimitives.WriteInt32LittleEndian(row[8..], pair.Matches.Count);
        hash.AppendData(row[..12]);
        foreach (var match in pair.Matches)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(row, match.Score);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], match.Manual ? 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..], match.GeometricInlier.HasValue ? match.GeometricInlier.Value ? 2 : 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(row[16..], match.A.Length);
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], match.B.Length);
            hash.AppendData(row);
            foreach (var value in match.A)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(row, value);
                hash.AppendData(row[..8]);
            }

            foreach (var value in match.B)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(row, value);
                hash.AppendData(row[..8]);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
