using System.ComponentModel.Composition;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IFeatureSetCacheFactory)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FeatureSetCacheFactory : IFeatureSetCacheFactory
{
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public FeatureSetCacheFactory(IJsonCodec json)
    {
        this.json = json;
    }

    public FeatureSetCache Create(long capacityBytes) => new(json, capacityBytes);
}
