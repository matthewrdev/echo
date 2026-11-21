using Echo.Models;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class MeshDepthViews
{
    public static DepthView[] Build(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps)
    {
        return new MeshDepthContext(project, scene, maps).Views;
    }
}
