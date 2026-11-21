namespace Echo.Storage.Pages;

internal sealed class CachedPage
{
    public required PageAddress Address
    {
        get; init;
    }
    public required byte[] Bytes
    {
        get; init;
    }
    public bool Dirty
    {
        get; set;
    }
}
