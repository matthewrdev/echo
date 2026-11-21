namespace Echo.Pipeline.Features;

public interface IFeatureSetCacheFactory
{
    FeatureSetCache Create(long capacityBytes);
}
