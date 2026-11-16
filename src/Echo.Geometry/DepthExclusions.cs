using Echo.Contracts;

namespace Echo.Geometry;

public static class DepthExclusions
{
    public static DepthMap Apply(Photo photo, CameraPose camera, DepthMap map, IReadOnlyList<ExclusionRegion> regions)
    {
        if (!regions.Any(region => region.Enabled))
        {
            return map;
        }
        return new DepthMap
        {
            ImageId = map.ImageId,
            Width = map.Width,
            Height = map.Height,
            Samples = new ExcludedDepthSamples(new DepthView(photo, camera, map), regions)
        };
    }
}
