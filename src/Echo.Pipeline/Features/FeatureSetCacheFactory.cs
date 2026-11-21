using Echo.Infrastructure;

namespace Echo.Pipeline.Features;

public sealed class FeatureSetCacheFactory : IFeatureSetCacheFactory
{
    private readonly IJsonCodec json;

    public FeatureSetCacheFactory(IJsonCodec json)
    {
        this.json = json;
    }

    public FeatureSetCache Create(long capacityBytes) => new(json, capacityBytes);
}
