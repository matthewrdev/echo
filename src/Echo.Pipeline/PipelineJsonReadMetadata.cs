using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Pipeline;

public sealed class PipelineJsonReadMetadata : IJsonReadMetadata
{
    private readonly PipelineJsonReadContext context;

    public PipelineJsonReadMetadata()
    {
        context = new PipelineJsonReadContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter<SubjectDistanceMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
            }
        });
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
