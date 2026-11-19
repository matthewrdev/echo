using Echo.Contracts;

namespace Echo.Pipeline;

public static class AlignmentPreviewSampler
{
    public static AlignmentPreview Sample(ReconstructionScene scene, int limit = 4000000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var count = Math.Min(limit, scene.Points.Count);
        var points = new List<CloudPoint>(count);
        for (var index = 0; index < count; index++)
        {
            points.Add(scene.Points[(int)((long)index * scene.Points.Count / count)]);
        }
        return new AlignmentPreview
        {
            TotalPoints = scene.Points.Count,
            Scene = new ReconstructionScene
            {
                Frame = scene.Frame,
                Origin = scene.Origin,
                Cameras = scene.Cameras,
                Points = points,
                Unplaced = scene.Unplaced,
                Warnings = scene.Warnings,
                Bundle = scene.Bundle
            }
        };
    }
}
