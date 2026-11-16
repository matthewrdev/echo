using System.ComponentModel.Composition;

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
        using var writer = new StreamWriter(path);
        writer.WriteLine($"ply\nformat ascii 1.0\nelement vertex {points.Count}\nproperty float x\nproperty float y\nproperty float z");
        if (faces != null)
        {
            writer.WriteLine($"element face {faces.Count}\nproperty list uchar int vertex_indices");
        }

        writer.WriteLine("end_header");
        foreach (var point in points)
        {
            writer.WriteLine(FormattableString.Invariant($"{point[0]:G9} {point[1]:G9} {point[2]:G9}"));
        }

        if (faces != null)
        {
            foreach (var face in faces)
            {
                writer.WriteLine($"3 {face[0]} {face[1]} {face[2]}");
            }
        }
    }
}
