using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Pipeline;

[Export(typeof(IImageWorkPlanner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ImageWorkPlanner : IImageWorkPlanner
{
    [ImportingConstructor]
    public ImageWorkPlanner()
    {
    }

    public ImageWorkPlan Features(Settings settings, IReadOnlyList<Photo> images)
    {
        long largest = 1;
        foreach (var image in images)
        {
            if (image.Width <= 0 || image.Height <= 0)
            {
                // Missing dimensions cannot justify running several decoders at once.
                largest = Math.Max(largest, MemoryBudget());
                continue;
            }
            var sourcePixels = (long)image.Width * image.Height;
            var scale = Math.Min(1.0, (double)settings.FeatureWidth / Math.Max(1, image.Width));
            var analysisPixels = (long)Math.Ceiling(image.Width * scale) * (long)Math.Ceiling(image.Height * scale);
            // Decoder storage, pooled scale-space planes, line scratch and retained descriptors.
            // This is a conservative scheduling estimate, not a process-wide memory limit.
            largest = Math.Max(largest, sourcePixels * 8 + analysisPixels * 160 + DescriptorBytes(settings));
        }

        // Point and line extraction already use two concurrent branches per image.
        var threadsPerImage = settings.DetectLines ? Math.Min(2, Environment.ProcessorCount) : 1;
        return Plan(images.Count, largest, threadsPerImage, 0);
    }

    public ImageWorkPlan Matching(Settings settings, int pairCount)
    {
        // Include two decoded inputs, correspondence objects and verification scratch.
        var bytesPerPair = DescriptorBytes(settings) * 4 + 16L * 1024 * 1024;
        var cacheBytes = Math.Min(64L * 1024 * 1024, MemoryBudget() / 4);
        return Plan(pairCount, bytesPerPair, 1, cacheBytes);
    }

    private ImageWorkPlan Plan(int count, long bytesPerWorker, int threadsPerWorker, long cacheBytes)
    {
        var processors = Math.Max(1, Environment.ProcessorCount - 2);
        var cpuWorkers = Math.Max(1, processors / threadsPerWorker);
        var memoryWorkers = Math.Max(1, (MemoryBudget() - cacheBytes) / bytesPerWorker);
        var workers = (int)Math.Max(1, Math.Min(Math.Min(count, cpuWorkers), memoryWorkers));
        return new ImageWorkPlan(workers, bytesPerWorker, cacheBytes);
    }

    private long MemoryBudget()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return Math.Clamp(available / 4, 64L * 1024 * 1024, 512L * 1024 * 1024);
    }

    private long DescriptorBytes(Settings settings)
    {
        return ((long)settings.FeatureLimit + settings.LineLimit) * 1024;
    }
}
