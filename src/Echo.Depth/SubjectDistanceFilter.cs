using Echo.Contracts;

namespace Echo.Depth;

public static class SubjectDistanceFilter
{
    public static DepthSearchRange Limit(DepthSearchRange range, SubjectDistancePlan plan)
    {
        if (!plan.MaximumDistance.HasValue || range.Far <= plan.MaximumDistance.Value)
        {
            return range;
        }

        var far = plan.MaximumDistance.Value;
        var outside = range.Near >= far;
        return range with
        {
            Near = outside ? far * .02 : range.Near,
            Far = far,
            Median = Math.Min(range.Median, far),
            Source = range.Source + "; " + plan.Source
        };
    }

    public static int Apply(DepthMap map, SubjectDistancePlan plan, CancellationToken cancellation)
    {
        if (!plan.MaximumDistance.HasValue)
        {
            return 0;
        }

        var removed = 0;
        for (var index = 0; index < map.Depth.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (map.Depth[index] > plan.MaximumDistance.Value)
            {
                map.Depth[index] = 0;
                map.Confidence[index] = 0;
                removed++;
            }
        }

        return removed;
    }
}
