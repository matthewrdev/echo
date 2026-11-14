using Echo.Contracts;

namespace Echo.Pipeline;

public interface IImageWorkPlanner
{
    ImageWorkPlan Features(Settings settings, IReadOnlyList<Photo> images);
    ImageWorkPlan Matching(Settings settings, int pairCount);
}
