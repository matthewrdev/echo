using Echo.Models;

namespace Echo.Geometry;

public static class DepthExclusions
{
    public static DepthMap Apply(Photo photo, CameraPose camera, DepthMap map, IReadOnlyList<ExclusionRegion> regions) =>
        Apply(photo, camera, map, new RegionCropFilter(regions));

    public static DepthMap Apply(Photo photo, CameraPose camera, DepthMap map, RegionCropFilter crop)
    {
        if (!crop.HasRegions)
        {
            return map;
        }

        return new DepthMap
        {
            ImageId = map.ImageId,
            Width = map.Width,
            Height = map.Height,
            Samples = new ExcludedDepthSamples(new DepthView(photo, camera, map), crop)
        };
    }
}
