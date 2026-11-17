using Echo.Geometry;

namespace Echo.Meshing;

internal sealed class VolumeCellBatch
{
    public const int Capacity = 16384;

    public VolumeCellBatch(bool accelerated)
    {
        Keys = new Grid[Capacity];
        Records = accelerated ? new int[Capacity * 12] : [];
    }

    public Grid[] Keys
    {
        get;
    }
    public int[] Records
    {
        get;
    }
    public int Count
    {
        get; set;
    }
    public int ActiveCount
    {
        get; set;
    }
    public bool Accelerated
    {
        get; set;
    }
}
