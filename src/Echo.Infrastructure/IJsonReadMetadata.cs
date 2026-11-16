using System.Text.Json.Serialization.Metadata;

namespace Echo.Infrastructure;

public interface IJsonReadMetadata
{
    JsonTypeInfo? GetTypeInfo(Type type);
}
