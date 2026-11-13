namespace Echo.Contracts;

public sealed class LineFeature : IFeatureDescriptor
{
    public double[] Start { get; set; } = [];
    public double[] End { get; set; } = [];
    public float[] Descriptor { get; set; } = [];
    public double Strength
    {
        get; set;
    }
}
