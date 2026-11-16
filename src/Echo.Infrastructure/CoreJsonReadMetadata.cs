using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Echo.Contracts;

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
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter<PerformanceBackend>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<PerformanceStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<PerformanceSpanKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<ComputeBackend>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<IslandTrimMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new JsonStringEnumConverter<ProcessingMemoryMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
            }
        });
    }

    public JsonTypeInfo? GetTypeInfo(Type type) => context.GetTypeInfo(type);
}
