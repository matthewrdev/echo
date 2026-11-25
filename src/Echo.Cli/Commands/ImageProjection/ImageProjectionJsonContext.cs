using System.Text.Json.Serialization;
using Echo.ImageProjection;

namespace Echo.Cli.Commands.ImageProjection;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ImageGeometryDocument))]
[JsonSerializable(typeof(ImageGeometryResult))]
internal sealed partial class ImageProjectionJsonContext : JsonSerializerContext
{
}
