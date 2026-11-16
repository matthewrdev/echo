using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Cli;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CliJsonMetadata : IJsonReadMetadata
{
    private readonly IJsonTypeInfoResolver resolver;

    [ImportingConstructor]
    public CliJsonMetadata()
    {
        resolver = CliJsonContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);
}
