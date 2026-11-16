using System.ComponentModel.Composition;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Echo.Infrastructure;

namespace Echo.Projects;

[Export(typeof(IJsonReadMetadata)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectsJsonMetadata : IJsonReadMetadata
{
    private readonly IJsonTypeInfoResolver resolver;

    [ImportingConstructor]
    public ProjectsJsonMetadata()
    {
        resolver = ProjectsJsonContext.Default;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);
}
