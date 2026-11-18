namespace Echo.Contracts;

public interface IFeatureDescriptor
{
    ReadOnlyMemory<float> DescriptorData
    {
        get;
    }
}
