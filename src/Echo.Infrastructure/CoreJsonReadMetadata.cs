using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Echo.Infrastructure;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CoreJsonReadMetadata : IJsonReadMetadata
{
    private readonly CoreJsonReadContext context;

    [ImportingConstructor]
    public CoreJsonReadMetadata()
    {
        context = new CoreJsonReadContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
