using Echo.Geometry;

namespace Echo.Optimization;

internal sealed class Track
{
    public V3 Point
    {
        get; set;
    }
    public List<Observation> Observations { get; set; } = [];
}
