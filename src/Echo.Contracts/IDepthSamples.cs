namespace Echo.Contracts;

public interface IDepthSamples
{
    int Count
    {
        get;
    }
    float DepthAt(int index);
    float ConfidenceAt(int index);
}
