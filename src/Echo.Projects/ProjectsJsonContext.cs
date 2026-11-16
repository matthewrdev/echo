using System.Text.Json.Serialization;

namespace Echo.Projects;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PhotoImportEvent))]
internal partial class ProjectsJsonContext : JsonSerializerContext
{
}
