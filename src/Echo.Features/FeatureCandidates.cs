using Echo.Contracts;

namespace Echo.Features;

internal sealed class FeatureCandidates
{
    public List<Corner> Corners { get; set; } = [];
    public List<LineFeature> Lines { get; set; } = [];
    public List<Feature> Junctions { get; set; } = [];
}
