namespace Echo.Models;

public interface IFeatureDescriptor
{
    ReadOnlyMemory<float> DescriptorData
    {
        get;
    }
}
