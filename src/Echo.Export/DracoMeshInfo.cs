using System.Runtime.InteropServices;

namespace Echo.Export;

[StructLayout(LayoutKind.Sequential)]
internal struct DracoMeshInfo
{
    public nint data;
    public int length;
    public int positionId;
    public int uvId;
    public int vertices;
    public int triangles;
    public int maximumIndex;
    public int degenerateUvs;
    public float minX;
    public float minY;
    public float minZ;
    public float maxX;
    public float maxY;
    public float maxZ;
}
