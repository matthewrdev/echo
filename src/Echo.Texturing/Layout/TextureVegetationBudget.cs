using SkiaSharp;

namespace Echo.Texturing.Layout;

public static class TextureVegetationBudget
{
    public static int Apply(IReadOnlyList<TexturePatch> patches, IReadOnlyList<bool>? vegetationFaces, double linearScale)
    {
        if (vegetationFaces == null)
        {
            return 0;
        }
        if (!double.IsFinite(linearScale) || linearScale is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(linearScale));
        }
        var count = 0;
        foreach (var patch in patches)
        {
            if (patch.Faces.Count == 0 || patch.Faces.Any(face => !vegetationFaces[face]))
            {
                continue;
            }
            foreach (var coordinate in patch.Coordinates.Values)
            {
                coordinate[0] *= linearScale;
                coordinate[1] *= linearScale;
            }
            var bounds = patch.Bounds;
            patch.Bounds = new SKRect((float)(bounds.Left * linearScale), (float)(bounds.Top * linearScale),
                (float)(bounds.Right * linearScale), (float)(bounds.Bottom * linearScale));
            count++;
        }
        return count;
    }
}
