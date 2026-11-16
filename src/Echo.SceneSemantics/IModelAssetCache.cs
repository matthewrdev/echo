namespace Echo.SceneSemantics;

public interface IModelAssetCache
{
    string EnsureAvailable(ModelAsset asset, string legacyDirectory, CancellationToken cancellation, Action<string>? progress = null);
}
