using Echo.Models;

namespace Echo.Features;

internal sealed class FeatureCandidates
{
    public List<Corner> Corners { get; set; } = [];
    public List<Feature> ScaleFeatures { get; set; } = [];
    public IReadOnlyList<LineFeature> Lines { get; set; } = [];
    public IReadOnlyList<Feature> Junctions { get; set; } = [];
}
