using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Metadata;

[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
public partial class MetadataJsonReadContext : JsonSerializerContext
{
}
