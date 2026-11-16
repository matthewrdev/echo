using System.Text.Json.Serialization;

namespace Echo.Export;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(GlbAccessor))]
[JsonSerializable(typeof(GisReference))]
[JsonSerializable(typeof(GlbAsset))]
[JsonSerializable(typeof(GlbBounds))]
[JsonSerializable(typeof(GlbBuffer))]
[JsonSerializable(typeof(GlbBufferView))]
[JsonSerializable(typeof(GlbDocument))]
[JsonSerializable(typeof(GlbEmbeddedTexture))]
[JsonSerializable(typeof(GlbExternalTexture))]
[JsonSerializable(typeof(GlbImage))]
[JsonSerializable(typeof(GlbInspectionReport))]
[JsonSerializable(typeof(GlbMaterial))]
[JsonSerializable(typeof(GlbMesh))]
[JsonSerializable(typeof(GlbMetallicRoughness))]
[JsonSerializable(typeof(GlbNode))]
[JsonSerializable(typeof(GlbPositionAccessor))]
[JsonSerializable(typeof(GlbPrimitive))]
[JsonSerializable(typeof(GlbSampler))]
[JsonSerializable(typeof(GlbScene))]
[JsonSerializable(typeof(GlbTargetBufferView))]
[JsonSerializable(typeof(GlbTexture))]
[JsonSerializable(typeof(GlbTextureReference))]
internal partial class ExportJsonContext : JsonSerializerContext
{
}
