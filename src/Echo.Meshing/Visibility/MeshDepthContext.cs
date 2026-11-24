using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Visibility;

// One immutable-input mesh run owns this preparation. Never retain it on the
// shared mesh builder: another run may have different poses, maps or exclusions.
public sealed class MeshDepthContext : IDisposable
{
    private readonly PageMemoryReservation? reservation;

    internal IReadOnlyDictionary<string, Photo> Photos
    {
        get;
    }
    internal DepthView[] Views
    {
        get;
    }

    public RegionCropFilter Crop
    {
        get;
    }

    public MeshDepthContext(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        WorkBuffers? work = null, CancellationToken cancellation = default)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        Photos = photos;
        Views = new DepthView[maps.Count];
        Crop = new RegionCropFilter(project.Settings.ExclusionRegions, project.Settings.GpsPolygons, scene.Origin);
        for (var index = 0; index < maps.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var map = maps[index];
            var photo = photos[map.ImageId];
            var camera = cameras[map.ImageId];
            Views[index] = new DepthView(photo, camera, DepthExclusions.Apply(photo, camera, map, Crop));
        }

        var bytes = Views.Where(view => view.Map.Samples != null).Sum(view => view.Map.SampleCount * 4L + 256);
        reservation = bytes == 0 ? null : work?.TryReserveMemory(bytes);
        if (reservation == null)
        {
            return;
        }

        try
        {
            for (var index = 0; index < Views.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var view = Views[index];
                var map = view.Map;
                if (map.Samples == null)
                {
                    continue;
                }

                var snapshot = new DepthMap
                {
                    ImageId = map.ImageId,
                    Width = map.Width,
                    Height = map.Height,
                    Samples = new MeshDepthSamples(map, cancellation)
                };
                Views[index] = new DepthView(photos[map.ImageId], view.Camera, snapshot);
            }
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (reservation != null)
        {
            Array.Clear(Views);
            reservation.Dispose();
        }
    }
}
