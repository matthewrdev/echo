using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

// One immutable-input mesh run owns this preparation. Never retain it on the
// shared MEF builder: another run may have different poses, maps or exclusions.
public sealed class MeshDepthContext
{
    internal IReadOnlyDictionary<string, Photo> Photos
    {
        get;
    }
    internal DepthView[] Views
    {
        get;
    }

    public MeshDepthContext(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        Photos = photos;
        Views = new DepthView[maps.Count];
        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];
            var photo = photos[map.ImageId];
            var camera = cameras[map.ImageId];
            Views[index] = new DepthView(photo, camera, DepthExclusions.Apply(photo, camera, map, project.Settings.ExclusionRegions));
        }
    }
}
