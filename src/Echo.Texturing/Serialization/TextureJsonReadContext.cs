using System.Text.Json.Serialization;

namespace Echo.Texturing.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TextureResult))]
public partial class TextureJsonReadContext : JsonSerializerContext
{
}
