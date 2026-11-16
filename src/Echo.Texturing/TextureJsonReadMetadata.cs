using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Texturing;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureJsonReadMetadata : IJsonReadMetadata
{
    private readonly IJsonTypeInfoResolver resolver;

    [ImportingConstructor]
    public TextureJsonReadMetadata()
    {
        resolver = TextureJsonReadContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);
}
