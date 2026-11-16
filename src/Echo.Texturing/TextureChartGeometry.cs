using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Texturing;

internal sealed class TextureChartGeometry
{
    public TextureChartGeometry(Surface surface, CancellationToken cancellation, WorkBuffers? work = null)
    {
        Surface = surface;
        var topology = SurfaceTopologyBuilder.Build(surface, cancellation, work);
        WeldedVertices = topology.WeldedVertices;
        Neighbours = topology.Neighbours;
        Points = work?.List(new Vector3RecordCodec()) ?? new List<V3>(surface.Vertices.Count);
        foreach (var vertex in surface.Vertices)
        {
            Points.Add(new V3(vertex));
        }
        Centres = work?.Filled(surface.Faces.Count, default(V3), new Vector3RecordCodec()) ?? new V3[surface.Faces.Count];
        Normals = work?.Filled(surface.Faces.Count, default(V3), new Vector3RecordCodec()) ?? new V3[surface.Faces.Count];
        Areas = work?.Filled(surface.Faces.Count, 0d, new DoubleRecordCodec()) ?? new double[surface.Faces.Count];
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
    public IList<int> WeldedVertices
    {
        get;
    }
    public IList<int> Neighbours
    {
        get;
    }
    public IList<V3> Points
    {
        get;
    }
    public IList<V3> Centres
    {
        get;
    }
    public IList<V3> Normals
    {
        get;
    }
    public IList<double> Areas
    {
        get;
    }
}
