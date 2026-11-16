using Echo.Contracts;

namespace Echo.Cli;

internal sealed class PhotoImportResult
{
    public required List<Photo> Imported
    {
        get; init;
    }
}
