using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal sealed class CollapseRegion
{
    public CollapseRegion(WorkBuffers? work, bool hasTolerances)
    {
        Work = work;
        Vertices = work?.List(new PositionRecordCodec()) ?? new List<V3>();
        Faces = work?.List(new FaceVerticesCodec()) ?? new List<FaceVertices>();
        OriginalVertices = work?.List(new IntRecordCodec()) ?? new List<int>();
        Locked = work?.List(new BoolRecordCodec()) ?? new List<bool>();
        OutputVertices = work?.List(new IntRecordCodec()) ?? new List<int>();
        OutputStates = work?.List(new VertexRecordCodec()) ?? new List<Vertex>();
        if (hasTolerances)
        {
            Tolerances = work?.List(new DoubleRecordCodec()) ?? new List<double>();
            OutputTolerances = work?.List(new DoubleRecordCodec()) ?? new List<double>();
        }
    }

    public WorkBuffers? Work
    {
        get;
    }
    public IList<V3> Vertices
    {
        get;
    }
    public IList<FaceVertices> Faces
    {
        get;
    }
    public IList<int> OriginalVertices
    {
        get;
    }
    public IList<bool> Locked
    {
        get;
    }
    public IList<double>? Tolerances
    {
        get;
    }
    public IList<int> OutputVertices
    {
        get;
    }
    public IList<Vertex> OutputStates
    {
        get;
    }
    public IList<double>? OutputTolerances
    {
        get;
    }
    public Surface? Reduced
    {
        get; set;
    }
    public Surface Surface => new()
    {
        Vertices = new RecordListView<V3, double[]>(Vertices, point => point.Array),
        Faces = new RecordListView<FaceVertices, int[]>(Faces, face => [face.A, face.B, face.C])
    };
}
