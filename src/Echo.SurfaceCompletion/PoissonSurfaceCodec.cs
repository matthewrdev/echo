using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

[Export(typeof(IPoissonSurfaceCodec)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PoissonSurfaceCodec : IPoissonSurfaceCodec
{
    [ImportingConstructor]
    public PoissonSurfaceCodec()
    {
    }

    public void WriteSamples(string path, IReadOnlyList<OrientedSurfaceSample> samples, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = File.Create(path);
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x49504345u);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 1u);
        BinaryPrimitives.WriteUInt64LittleEndian(header[8..], (ulong)samples.Count);
        stream.Write(header);
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            for (var start = 0; start < samples.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length / 48, samples.Count - start);
                for (var index = 0; index < count; index++)
                {
                    var sample = samples[start + index];
                    var bytes = buffer.AsSpan(index * 48, 48);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes, sample.Position.X);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes[8..], sample.Position.Y);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes[16..], sample.Position.Z);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes[24..], sample.Normal.X);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes[32..], sample.Normal.Y);
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes[40..], sample.Normal.Z);
                }
                stream.Write(buffer.AsSpan(0, count * 48));
                start += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Surface Read(string path, CancellationToken cancellation, WorkBuffers? work = null)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x4f504345u || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 1u)
        {
            throw new EchoException("Invalid completed-surface header.", 5);
        }
        var vertices = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        var faces = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        var validCounts = vertices > 0 && vertices <= 10000000 && faces > 0 && faces <= 20000000;
        if (!validCounts || stream.Length != 24L + (long)vertices * 24 + (long)faces * 12)
        {
            throw new EchoException("Invalid completed-surface size.", 5);
        }
        var surface = work?.Surface() ?? new Surface
        {
            Vertices = new List<double[]>((int)vertices),
            Faces = new List<int[]>((int)faces)
        };
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            for (var start = 0; start < (int)vertices;)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length / 24, (int)vertices - start);
                stream.ReadExactly(buffer.AsSpan(0, count * 24));
                for (var index = 0; index < count; index++)
                {
                    var bytes = buffer.AsSpan(index * 24, 24);
                    var x = BinaryPrimitives.ReadDoubleLittleEndian(bytes);
                    var y = BinaryPrimitives.ReadDoubleLittleEndian(bytes[8..]);
                    var z = BinaryPrimitives.ReadDoubleLittleEndian(bytes[16..]);
                    var finite = double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
                    if (!finite)
                    {
                        throw new EchoException("The completed surface contains a non-finite vertex.", 5);
                    }
                    if (surface.Vertices is not PagedList<double[]>)
                    {
                        surface.Vertices.Add([x, y, z]);
                    }
                }
                if (surface.Vertices is PagedList<double[]> pagedVertices)
                {
                    pagedVertices.AddEncodedRecords(buffer.AsSpan(0, count * 24));
                }
                start += count;
            }
            for (var start = 0; start < (int)faces;)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length / 12, (int)faces - start);
                stream.ReadExactly(buffer.AsSpan(0, count * 12));
                for (var index = 0; index < count; index++)
                {
                    var bytes = buffer.AsSpan(index * 12, 12);
                    var a = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                    var b = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
                    var c = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
                    var inRange = a < vertices && b < vertices && c < vertices;
                    var distinct = a != b && a != c && b != c;
                    if (!inRange || !distinct)
                    {
                        throw new EchoException("The completed surface contains an invalid triangle.", 5);
                    }
                    if (surface.Faces is not PagedList<int[]>)
                    {
                        surface.Faces.Add([(int)a, (int)b, (int)c]);
                    }
                }
                if (surface.Faces is PagedList<int[]> pagedFaces)
                {
                    pagedFaces.AddEncodedRecords(buffer.AsSpan(0, count * 12));
                }
                start += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return surface;
    }
}
