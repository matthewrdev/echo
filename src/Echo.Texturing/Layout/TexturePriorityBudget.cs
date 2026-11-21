using Echo.Models;
using Echo.Geometry;
using SkiaSharp;

namespace Echo.Texturing.Layout;

internal static class TexturePriorityBudget
{
    public static int[]? Classify(Surface mesh, TexturePriorityPainting? painting, CancellationToken cancellation)
    {
        if (painting == null || painting.Strokes.Count == 0)
        {
            return null;
        }
        var field = new TexturePriorityField(painting, cancellation);
        var influences = new SortedDictionary<int, double>();
        var bands = new int[mesh.Faces.Count];
        for (var index = 0; index < bands.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = mesh.Faces[index];
            var a = new V3(mesh.Vertices[face[0]]);
            var b = new V3(mesh.Vertices[face[1]]);
            var c = new V3(mesh.Vertices[face[2]]);
            var weight = (field.LogWeight(a, influences) + field.LogWeight(b, influences)
                + field.LogWeight(c, influences) + 3 * field.LogWeight((a + b + c) / 3, influences)) / 6;
            // Five broad bands avoid a new UV seam for every tiny brush variation.
            bands[index] = Math.Clamp((int)Math.Round(weight, MidpointRounding.AwayFromZero), -2, 2);
        }
        return bands.Any(band => band != 0) ? bands : null;
    }

    public static void Apply(IReadOnlyList<TexturePatch> patches, IReadOnlyList<int>? bands)
    {
        if (bands == null)
        {
            return;
        }
        foreach (var patch in patches)
        {
            // Weight is an area budget; UV lengths therefore use its square root.
            var scale = Math.Pow(2, bands[patch.Faces[0]] / 2d);
            foreach (var coordinate in patch.Coordinates.Values)
            {
                coordinate[0] *= scale;
                coordinate[1] *= scale;
            }
            var bounds = patch.Bounds;
            patch.Bounds = new SKRect((float)(bounds.Left * scale), (float)(bounds.Top * scale),
                (float)(bounds.Right * scale), (float)(bounds.Bottom * scale));
        }
    }
}
