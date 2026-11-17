using System.ComponentModel.Composition;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PipelineJsonReadMetadata : IJsonReadMetadata
{
    private readonly PipelineJsonReadContext context = new();

    [ImportingConstructor]
    public PipelineJsonReadMetadata()
    {
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
