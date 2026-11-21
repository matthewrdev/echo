using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed class LineFeature : IFeatureDescriptor
{
    private float[]? descriptor;
    private ReadOnlyMemory<float> descriptorData;
    public double[] Start { get; set; } = [];
    public double[] End { get; set; } = [];
    public float[] Descriptor
    {
        get
        {
            if (descriptor == null)
            {
                descriptor = descriptorData.ToArray();
                descriptorData = descriptor;
            }
            return descriptor;
        }
        set
        {
            descriptor = value;
            descriptorData = value;
        }
    }

    [JsonIgnore]
    public ReadOnlyMemory<float> DescriptorData
    {
        get => descriptorData;
        set
        {
            descriptor = null;
            descriptorData = value;
        }
    }
    public double Strength
    {
        get; set;
    }
}
