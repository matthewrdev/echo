namespace Echo.Compute.Metal;

internal sealed class DescriptorBufferIdentity
{
    public DescriptorBufferIdentity(uint value)
    {
        Value = value;
    }

    public uint Value
    {
        get;
    }
}
