using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Texturing;

internal sealed class TextureChartGeometry
{
    public TextureChartGeometry(Surface surface, CancellationToken cancellation)
    {
        Surface = surface;
        var topology = SurfaceTopologyBuilder.Build(surface, cancellation);
        WeldedVertices = topology.WeldedVertices;
        Neighbours = topology.Neighbours;
        Points = surface.Vertices.Select(vertex => new V3(vertex)).ToArray();
        Centres = new V3[surface.Faces.Count];
        Normals = new V3[surface.Faces.Count];
        Areas = new double[surface.Faces.Count];
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = surface.Faces[index];
            var a = Points[face[0]];
            var b = Points[face[1]];
            var c = Points[face[2]];
            var normal = (b - a).Cross(c - a);
            Centres[index] = (a + b + c) / 3;
            Normals[index] = normal.Unit;
            Areas[index] = normal.Length * .5;
        }
    }

    public Surface Surface
    {
        get;
    }
    public int[] WeldedVertices
    {
        get;
    }
    public int[] Neighbours
    {
        get;
    }
    public V3[] Points
    {
        get;
    }
    public V3[] Centres
    {
        get;
    }
    public V3[] Normals
    {
        get;
    }
    public double[] Areas
    {
        get;
    }
}
