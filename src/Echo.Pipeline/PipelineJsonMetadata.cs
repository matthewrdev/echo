using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PipelineJsonMetadata : IJsonReadMetadata
{
    private readonly IJsonTypeInfoResolver resolver;

    [ImportingConstructor]
    public PipelineJsonMetadata()
    {
        resolver = PipelineJsonContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);
}
