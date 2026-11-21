using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Validation;

public sealed class ValidationJsonMetadata : IJsonReadMetadata
{
    private readonly ValidationJsonContext context;

    public ValidationJsonMetadata()
    {
        context = ValidationJsonContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
