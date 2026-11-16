using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel.Composition;
using System.Text;
using Echo.Storage;

namespace Echo.Pipeline;

[Export(typeof(IPlyWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PlyWriter : IPlyWriter
{
    [ImportingConstructor]
    public PlyWriter()
    {
    }

    public void Write(string path, IList<double[]> points, IList<int[]>? faces = null)
    {
        using var stream = File.Create(path);
        var header = FormattableString.Invariant($"ply\nformat binary_little_endian 1.0\nelement vertex {points.Count}\nproperty float x\nproperty float y\nproperty float z\n");
        if (faces != null)
        {
            header += FormattableString.Invariant($"element face {faces.Count}\nproperty list uchar int vertex_indices\n");
        }
        stream.Write(Encoding.ASCII.GetBytes(header + "end_header\n"));
        var input = ArrayPool<byte>.Shared.Rent(65536);
        var output = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            WriteVertices(stream, points, input, output);
            if (faces != null)
            {
                WriteFaces(stream, faces, input, output);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
            ArrayPool<byte>.Shared.Return(output);
        }
    }

    private void WriteVertices(Stream stream, IList<double[]> points, byte[] input, byte[] output)
    {
        var paged = points as PagedList<double[]>;
        var encoded = paged?.Codec is VectorRecordCodec;
        var capacity = Math.Min(input.Length / 24, output.Length / 12);
        for (var start = 0; start < points.Count;)
        {
            var count = Math.Min(capacity, points.Count - start);
            if (encoded)
            {
                paged!.ReadEncodedRecords(start, input.AsSpan(0, count * 24));
            }
            for (var index = 0; index < count; index++)
            {
                var point = encoded ? null : points[start + index];
                for (var axis = 0; axis < 3; axis++)
                {
                    var coordinate = encoded
                        ? BinaryPrimitives.ReadDoubleLittleEndian(input.AsSpan(index * 24 + axis * 8, 8))
                        : point![axis];
                    BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(index * 12 + axis * 4, 4), (float)coordinate);
                }
            }
            stream.Write(output.AsSpan(0, count * 12));
            start += count;
        }
    }

    private void WriteFaces(Stream stream, IList<int[]> faces, byte[] input, byte[] output)
    {
        var paged = faces as PagedList<int[]>;
        var encoded = paged?.Codec is TriangleRecordCodec;
        var capacity = Math.Min(input.Length / 12, output.Length / 13);
        for (var start = 0; start < faces.Count;)
        {
            var count = Math.Min(capacity, faces.Count - start);
            if (encoded)
            {
                paged!.ReadEncodedRecords(start, input.AsSpan(0, count * 12));
            }
            for (var index = 0; index < count; index++)
            {
                var record = output.AsSpan(index * 13, 13);
                record[0] = 3;
                if (encoded)
                {
                    input.AsSpan(index * 12, 12).CopyTo(record[1..]);
                }
                else
                {
                    var face = faces[start + index];
                    for (var corner = 0; corner < 3; corner++)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(record[(1 + corner * 4)..], face[corner]);
                    }
                }
            }
            stream.Write(output.AsSpan(0, count * 13));
            start += count;
        }
    }
}
