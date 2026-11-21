using System.Text.Json;

namespace Echo.Pipeline.Artifacts;

internal static class ProcessingArtifactReferences
{
    internal static List<string> Find(object inputs, JsonSerializerOptions options)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        Collect(JsonSerializer.SerializeToElement(inputs, options), references);
        return references.Order(StringComparer.Ordinal).ToList();
    }

    private static void Collect(JsonElement element, HashSet<string> references)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Collect(property.Value, references);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
            {
                Collect(value, references);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString()!;
            if (value.StartsWith("artifacts/", StringComparison.Ordinal) && value.Split('/').Length == 3)
            {
                references.Add(value);
            }
        }
    }
}
