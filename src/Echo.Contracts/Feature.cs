using System.Text.Json.Serialization;

namespace Echo.Contracts;

public sealed class Feature : IFeatureDescriptor
{
    private float[]? descriptor;
    private ReadOnlyMemory<float> descriptorData;
    public string Kind { get; set; } = "corner";
    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
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
}
