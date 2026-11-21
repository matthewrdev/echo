using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Texturing.Serialization;

public sealed class TextureJsonReadMetadata : IJsonReadMetadata
{
    private readonly TextureJsonReadContext context;

    public TextureJsonReadMetadata()
    {
        context = new TextureJsonReadContext(new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
