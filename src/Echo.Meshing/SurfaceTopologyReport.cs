namespace Echo.Meshing;

public sealed class SurfaceTopologyReport
{
    public int MainHullFaces
    {
        get; set;
    }
    public int BoundaryEdges
    {
        get; set;
    }
    public int NonManifoldEdges
    {
        get; set;
    }
    public int WindingConflicts
    {
        get; set;
    }
    public int DegenerateFaces
    {
        get; set;
    }
    public List<SurfaceBoundaryReport> Boundaries { get; set; } = [];
    public int InteriorHoles => Boundaries.Count(boundary => boundary.Interior);
    public bool Closed
    {
        get
        {
            var manifold = NonManifoldEdges == 0 && WindingConflicts == 0 && DegenerateFaces == 0;
            return MainHullFaces > 0 && BoundaryEdges == 0 && manifold;
        }
    }
}
