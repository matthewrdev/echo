using System.Text.Json.Serialization;

namespace Echo.Rendering;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModelRenderRequest))]
[JsonSerializable(typeof(ModelRenderResult))]
internal sealed partial class RenderJsonContext : JsonSerializerContext
{
}
