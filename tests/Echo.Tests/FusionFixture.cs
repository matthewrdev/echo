using Echo.Models;
using Echo.Geometry;

namespace Echo.Tests;

internal sealed class FusionFixture
{
    public ProjectData Project { get; } = new();
    public ReconstructionScene Scene { get; } = new();
    public List<DepthMap> Maps { get; } = [];
    public List<DepthView> Views { get; } = [];
    public DenseCloudData Cloud { get; } = new();

    public FusionFixture()
    {
        foreach (var id in new[] { "a", "b" })
        {
            var photo = new Photo { Id = id, Width = 64, Height = 64, FocalPixels = 64 };
            var camera = new CameraPose { ImageId = id, Centre = [id == "a" ? -.25 : .25, 0, 0] };
            var map = new DepthMap { ImageId = id, Width = 64, Height = 64, Depth = Enumerable.Repeat(10f, 64 * 64).ToArray() };
            Project.Images.Add(photo);
            Scene.Cameras.Add(camera);
            Maps.Add(map);
            Views.Add(new DepthView(photo, camera, map));
            Cloud.ImageIds.Add(id);
        }

        for (var x = -6; x <= 6; x++)
        {
            for (var y = -6; y <= 6; y++)
            {
                Cloud.Samples.Add(new DenseSample(x * .25f, y * .25f, 10, 0, 0, 0, 0, 1));
            }
        }

        Cloud.InputSamples = Cloud.Samples.Count;
    }
}
