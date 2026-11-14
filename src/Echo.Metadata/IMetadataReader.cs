using System.Text.Json;

namespace Echo.Metadata;

public interface IMetadataReader
{
    string Description
    {
        get;
    }
    Dictionary<string, JsonElement> Read(string path, bool includeAllGroups = false);
}
