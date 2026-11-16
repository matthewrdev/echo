using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Echo.Infrastructure;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CoreJsonReadMetadata : IJsonReadMetadata
{
    private readonly IJsonTypeInfoResolver resolver;

    [ImportingConstructor]
    public CoreJsonReadMetadata()
    {
        resolver = CoreJsonReadContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);
}
