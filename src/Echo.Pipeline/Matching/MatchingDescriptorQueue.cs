using Echo.Matching;
using Echo.Models;

namespace Echo.Pipeline.Matching;

// The ordered pair executor bounds the number of outstanding jobs. Only two
// descriptor jobs can reach Metal at a time, leaving the other pair workers
// free to verify and publish completed correspondences.
internal sealed class MatchingDescriptorQueue : IDisposable
{
    private readonly ConcurrentExclusiveSchedulerPair scheduler = new(TaskScheduler.Default, maxConcurrencyLevel: 2);
    private readonly IFeatureMatcher matcher;

    public MatchingDescriptorQueue(IFeatureMatcher matcher)
    {
        this.matcher = matcher;
    }

    public ImagePair Match(FeatureSet left, FeatureSet right, Settings settings, CancellationToken cancellation)
    {
        return Task.Factory.StartNew(() => matcher.Match(left, right, settings, cancellation), cancellation,
            TaskCreationOptions.DenyChildAttach, scheduler.ConcurrentScheduler).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        scheduler.Complete();
        scheduler.Completion.GetAwaiter().GetResult();
    }
}
