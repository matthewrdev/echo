using System.Text;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Texturing.Layout;
using SkiaSharp;

namespace Echo.Pipeline.Texture;

public sealed class TextureLayoutStore : ITextureLayoutStore
{
    private const int LayoutV1 = 0x31594c54;
    private readonly IFileOperations files;

    public TextureLayoutStore(IFileOperations files)
    {
        this.files = files;
    }

    public void Write(string path, TextureLayout layout, CancellationToken cancellation)
    {
        files.AtomicWrite(path, stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(LayoutV1);
            writer.Write(layout.MinimumAtlasSize);
            writer.Write(layout.GutterPixels);
            writer.Write(layout.VegetationChartCount);
            writer.Write(layout.TexelsPerWorldUnit);
            writer.Write(layout.BaselineTexelsPerWorldUnit);
            writer.Write(layout.Patches.Count);
            foreach (var patch in layout.Patches)
            {
                cancellation.ThrowIfCancellationRequested();
                writer.Write(patch.QuarterTurns);
                WriteRect(writer, patch.Bounds);
                WriteRect(writer, patch.AtlasBounds);
                writer.Write(patch.Faces.Count);
                foreach (var face in patch.Faces)
                {
                    writer.Write(face);
                }

                writer.Write(patch.Coordinates.Count);
                foreach (var coordinate in patch.Coordinates)
                {
                    writer.Write(coordinate.Key);
                    writer.Write(coordinate.Value[0]);
                    writer.Write(coordinate.Value[1]);
                }
            }
        });
    }

    public TextureLayout Read(string path, Surface mesh, CancellationToken cancellation)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != LayoutV1)
        {
            throw new InvalidDataException("Unsupported texture layout checkpoint.");
        }

        var atlasSize = reader.ReadInt32();
        var gutter = reader.ReadInt32();
        var vegetationCount = reader.ReadInt32();
        var scale = reader.ReadDouble();
        var baseline = reader.ReadDouble();
        var count = ReadCount(reader, mesh.Faces.Count);
        var patches = new TexturePatch[count];
        var remainingFaces = mesh.Faces.Count;
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var patch = new TexturePatch
            {
                QuarterTurns = reader.ReadInt32(),
                Bounds = ReadRect(reader),
                AtlasBounds = ReadRect(reader)
            };
            var faces = ReadCount(reader, remainingFaces);
            patch.Faces.EnsureCapacity(faces);
            patch.Triangles.EnsureCapacity(faces);
            for (var entry = 0; entry < faces; entry++)
            {
                var face = ReadCount(reader, mesh.Faces.Count - 1);
                patch.Faces.Add(face);
                patch.Triangles.Add(mesh.Faces[face]);
            }

            remainingFaces -= faces;
            var vertices = ReadCount(reader, (int)Math.Min(mesh.Vertices.Count, (long)faces * 3));
            patch.Coordinates.EnsureCapacity(vertices);
            for (var entry = 0; entry < vertices; entry++)
            {
                patch.Coordinates.Add(ReadCount(reader, mesh.Vertices.Count - 1), [reader.ReadDouble(), reader.ReadDouble()]);
            }

            patches[index] = patch;
        }

        if (remainingFaces != 0 || stream.Position != stream.Length)
        {
            throw new InvalidDataException("Texture layout does not cover the saved mesh.");
        }

        return new TextureLayout
        {
            MinimumAtlasSize = atlasSize,
            GutterPixels = gutter,
            VegetationChartCount = vegetationCount,
            TexelsPerWorldUnit = scale,
            BaselineTexelsPerWorldUnit = baseline,
            Patches = patches
        };
    }

    private int ReadCount(BinaryReader reader, int maximum)
    {
        var value = reader.ReadInt32();
        if (value < 0 || value > maximum)
        {
            throw new InvalidDataException("Texture layout record is outside the saved mesh.");
        }

        return value;
    }

    private void WriteRect(BinaryWriter writer, SKRect rect)
    {
        writer.Write(rect.Left);
        writer.Write(rect.Top);
        writer.Write(rect.Right);
        writer.Write(rect.Bottom);
    }

    private SKRect ReadRect(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
