namespace Echo.Metadata;

public interface IMetadataPayloadReader
{
    byte[] Read(string path, string tag);
}
