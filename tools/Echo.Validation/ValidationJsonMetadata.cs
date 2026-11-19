using System.ComponentModel.Composition;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Validation;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ValidationJsonMetadata : IJsonReadMetadata
{
    private readonly ValidationJsonContext context;

    [ImportingConstructor]
    public ValidationJsonMetadata()
    {
        context = ValidationJsonContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
