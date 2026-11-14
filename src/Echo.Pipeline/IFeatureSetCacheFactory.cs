namespace Echo.Pipeline;

public interface IFeatureSetCacheFactory
{
    FeatureSetCache Create(long capacityBytes);
}
