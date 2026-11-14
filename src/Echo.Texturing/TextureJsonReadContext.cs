using System.Text.Json.Serialization;

namespace Echo.Texturing;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TextureResult))]
public partial class TextureJsonReadContext : JsonSerializerContext
{
}
