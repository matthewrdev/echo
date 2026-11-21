using Echo.Models;

namespace Echo.Pipeline.Features;

public interface IImageWorkPlanner
{
    ImageWorkPlan Features(Settings settings, IReadOnlyList<Photo> images);
    ImageWorkPlan Matching(Settings settings, int pairCount);
}
