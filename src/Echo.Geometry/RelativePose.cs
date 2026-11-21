using Echo.Models;

namespace Echo.Geometry;

public sealed class RelativePose
{
    public double[] Rotation { get; set; } = [];
    public double[] Translation { get; set; } = [];
    public List<Match> Inliers { get; set; } = [];
}
