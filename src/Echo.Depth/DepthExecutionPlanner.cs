using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Depth;

[Export(typeof(IDepthExecutionPlanner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthExecutionPlanner : IDepthExecutionPlanner
{
    [ImportingConstructor]
    public DepthExecutionPlanner()
    {
    }

    public DepthExecutionPlan Plan(ProjectData project, int cameraCount)
    {
        var settings = project.Settings;
        long largestSource = 1;
        long largestMap = 1;
        foreach (var photo in project.Images)
        {
            largestSource = Math.Max(largestSource, (long)photo.Width * photo.Height);
            var scale = Math.Min(1.0, (double)settings.DepthWidth / Math.Max(1, photo.Width));
            var pixels = (long)Math.Ceiling(photo.Width * scale) * (long)Math.Ceiling(photo.Height * scale);
            largestMap = Math.Max(largestMap, pixels);
        }
        var neighbours = Math.Min(settings.DepthNeighbours, Math.Max(0, cameraCount - 1));
        // Decoded sources, transient decoder storage, pyramid levels and output arrays.
        // Pooled grayscale buffers can round up to twice the requested pixel count.
        // This is a scheduling estimate rather than a hard process-memory limit.
        var bytesPerImage = largestSource * 4 * (neighbours + 3) + largestMap * (36 + neighbours * 8);
        var memorySlots = Math.Max(1, settings.DepthMemoryBudgetMb * 1024L * 1024 / Math.Max(1, bytesPerImage));
        var workers = settings.DepthWorkerThreads > 0 ? settings.DepthWorkerThreads : Math.Max(1, Environment.ProcessorCount - 2);
        var images = (int)Math.Max(1, Math.Min(Math.Min(settings.DepthConcurrency, cameraCount), Math.Min(workers, memorySlots)));
        return new DepthExecutionPlan(images, Math.Max(1, workers / images), bytesPerImage);
    }
}
