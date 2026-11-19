namespace Echo.Validation;

public sealed class ValidationPhoto
{
    public required string File
    {
        get; set;
    }
    public required string Sha256
    {
        get; set;
    }
    public required long Bytes
    {
        get; set;
    }
}
