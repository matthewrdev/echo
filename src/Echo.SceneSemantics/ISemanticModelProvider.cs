namespace Echo.SceneSemantics;

public interface ISemanticModelProvider
{
    string Fingerprint
    {
        get;
    }
    string EnsureAvailable(string directory, CancellationToken cancellation, Action<string>? progress = null);
}
