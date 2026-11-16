using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class MeshDepthViews
{
    public static DepthView[] Build(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        return maps.Select(map => new DepthView(photos[map.ImageId], cameras[map.ImageId],
            DepthExclusions.Apply(photos[map.ImageId], cameras[map.ImageId], map, project.Settings.ExclusionRegions))).ToArray();
    }
}
