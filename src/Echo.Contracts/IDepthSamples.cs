namespace Echo.Contracts;

public interface IDepthSamples
{
    int Count
    {
        get;
    }
    float DepthAt(int index);
    void ReadDepth(int start, Span<float> destination);
    float ConfidenceAt(int index);
}
