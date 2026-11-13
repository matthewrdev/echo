using System.Text.Json;

namespace Echo.Metadata;

public interface IMetadataReader
{
    string? FindExifTool();
    Dictionary<string, JsonElement> Read(string path, bool includeAllGroups = false);
}
