using System.ComponentModel.Composition;
using Echo.Contracts;

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
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x49504345u);
        writer.Write(1u);
        writer.Write((ulong)samples.Count);
        foreach (var sample in samples)
        {
            cancellation.ThrowIfCancellationRequested();
            writer.Write(sample.Position.X);
            writer.Write(sample.Position.Y);
            writer.Write(sample.Position.Z);
            writer.Write(sample.Normal.X);
            writer.Write(sample.Normal.Y);
            writer.Write(sample.Normal.Z);
        }
    }

    public Surface Read(string path, CancellationToken cancellation)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x4f504345u || reader.ReadUInt32() != 1u)
        {
            throw new EchoException("Invalid completed-surface header.", 5);
        }
        var vertices = reader.ReadUInt64();
        var faces = reader.ReadUInt64();
        var validCounts = vertices > 0 && vertices <= 10000000 && faces > 0 && faces <= 20000000;
        if (!validCounts || reader.BaseStream.Length != 24L + (long)vertices * 24 + (long)faces * 12)
        {
            throw new EchoException("Invalid completed-surface size.", 5);
        }
        var surface = new Surface();
        for (ulong index = 0; index < vertices; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = new[] { reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble() };
            if (point.Any(value => !double.IsFinite(value)))
            {
                throw new EchoException("The completed surface contains a non-finite vertex.", 5);
            }
            surface.Vertices.Add(point);
        }
        for (ulong index = 0; index < faces; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = new[] { reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32() };
            if (face.Any(vertex => vertex >= vertices) || face.Distinct().Count() != 3)
            {
                throw new EchoException("The completed surface contains an invalid triangle.", 5);
            }
            surface.Faces.Add([(int)face[0], (int)face[1], (int)face[2]]);
        }
        return surface;
    }
}
