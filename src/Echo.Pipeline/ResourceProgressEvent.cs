using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class ResourceProgressEvent
{
    public required string Stage
    {
        get; init;
    }
    public required ResourceProgress Resources
    {
        get; init;
    }
}
