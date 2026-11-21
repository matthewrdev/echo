using Echo.Models;

namespace Echo.Pipeline.Features;

public sealed class ImageWorkPlanner : IImageWorkPlanner
{
    public ImageWorkPlanner()
    {
    }

    public ImageWorkPlan Features(Settings settings, IReadOnlyList<Photo> images)
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var requested = (long)settings.ProcessingMemoryBudgetMb * 1024 * 1024;
        // Leave headroom for the runtime, checkpoints and the viewer/native workers.
        var budget = Math.Max(1, Math.Min(available / 2, requested > 0 ? requested * 3 / 4 : available / 4));
        long largest = 1;
        foreach (var image in images)
        {
            if (image.Width <= 0 || image.Height <= 0)
            {
                // Missing dimensions cannot justify running several decoders at once.
                largest = Math.Max(largest, budget);
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
        return Plan(images.Count, largest, threadsPerImage, 0, budget);
    }

    public ImageWorkPlan Matching(Settings settings, int pairCount)
    {
        // Include two decoded inputs, correspondence objects and verification scratch.
        var bytesPerPair = DescriptorBytes(settings) * 4 + 16L * 1024 * 1024;
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var requested = (long)settings.ProcessingMemoryBudgetMb * 1024 * 1024;
        var budget = requested > 0 ? Math.Min(available, requested) : available / 4;
        var cacheBytes = Math.Clamp(budget / 8, 16L * 1024 * 1024, 512L * 1024 * 1024);
        return Plan(pairCount, bytesPerPair, 1, cacheBytes, Math.Max(bytesPerPair + cacheBytes, Math.Min(budget, cacheBytes + MemoryBudget())));
    }

    private ImageWorkPlan Plan(int count, long bytesPerWorker, int threadsPerWorker, long cacheBytes, long memoryBudget = 0)
    {
        var processors = Math.Max(1, Environment.ProcessorCount - 2);
        var cpuWorkers = Math.Max(1, processors / threadsPerWorker);
        var memoryWorkers = Math.Max(1, ((memoryBudget > 0 ? memoryBudget : MemoryBudget()) - cacheBytes) / bytesPerWorker);
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
