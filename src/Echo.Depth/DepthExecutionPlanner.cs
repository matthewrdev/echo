using Echo.Models;

namespace Echo.Depth;

public sealed class DepthExecutionPlanner : IDepthExecutionPlanner
{
    public DepthExecutionPlanner()
    {
    }

    public DepthExecutionPlan Plan(ProjectData project, int cameraCount, long jobBudgetBytes = 0, int? depthMemoryBudgetMb = null)
    {
        var settings = project.Settings;
        long largestSource = 1;
        long largestMap = 1;
        foreach (var photo in project.Images)
        {
            if (!photo.Enabled)
            {
                continue;
            }

            largestSource = Math.Max(largestSource, (long)photo.Width * photo.Height);
            var scale = Math.Min(1.0, (double)settings.DepthWidth / Math.Max(1, photo.Width));
            var pixels = (long)Math.Ceiling(photo.Width * scale) * (long)Math.Ceiling(photo.Height * scale);
            largestMap = Math.Max(largestMap, pixels);
        }

        var neighbours = DepthNeighbourSelector.MaximumNeighbours(settings.DepthNeighbours, cameraCount);
        // Decoded sources, transient decoder storage, pyramid levels and output arrays.
        // Pooled grayscale buffers can round up to twice the requested pixel count.
        // This is a scheduling estimate rather than a hard process-memory limit.
        var baseBytes = largestSource * 4 * (neighbours + 3) + largestMap * (36 + neighbours * 8);
        var workers = settings.DepthWorkerThreads > 0 ? settings.DepthWorkerThreads : Math.Max(1, Environment.ProcessorCount - 2);
        long workerBytes = 0;
        if (settings.DepthBackend != "cpu")
        {
            // Packed upload plus resident GPU grayscale, and in-flight CPU/GPU batches.
            // Keep per-worker scratch separate so admission can reduce parallelism.
            baseBytes += largestMap * neighbours * 8;
            workerBytes = DepthBatchSizing.WorkerBytes(settings.DepthWidth,
                Math.Max(settings.DepthPlanes, settings.DepthRefinementSamples), settings.DepthRefinementSamples);
        }

        var configured = depthMemoryBudgetMb ?? settings.DepthMemoryBudgetMb;
        var memoryBudgetBytes = configured == 0 && jobBudgetBytes > 0
            ? jobBudgetBytes
            : DepthMemoryBudget.ResolveMegabytes(configured, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes) * 1048576L;
        if (jobBudgetBytes > 0)
        {
            memoryBudgetBytes = Math.Min(memoryBudgetBytes, jobBudgetBytes);
        }

        var requestedImages = settings.DepthConcurrency == 0 ? Math.Min(8, Math.Max(1, workers / 2)) : settings.DepthConcurrency;
        var maximumImages = Math.Max(1, Math.Min(Math.Min(requestedImages, cameraCount), workers));
        // Schedule the actual worker share for each candidate, not all workers per image.
        // Fewer workers reduce in-flight Metal batches without changing reconstruction quality.
        for (var images = maximumImages; images >= 1; images--)
        {
            var perImageBudget = memoryBudgetBytes / images;
            var perImageWorkers = Math.Max(1, workers / images);
            if (workerBytes > 0)
            {
                perImageWorkers = (int)Math.Min(perImageWorkers, Math.Max(0, (perImageBudget - baseBytes) / workerBytes));
            }

            var bytesPerImage = baseBytes + workerBytes * perImageWorkers;
            if (perImageWorkers >= 1 && bytesPerImage <= perImageBudget)
            {
                return new DepthExecutionPlan(images, perImageWorkers, bytesPerImage, memoryBudgetBytes);
            }
        }

        var minimumBytes = baseBytes + workerBytes;
        var jobIsSmaller = jobBudgetBytes > 0 && jobBudgetBytes <= configured * 1048576L;
        var limitedByJob = configured == 0 || jobIsSmaller;
        var advice = limitedByJob
            ? "Increase the project processing memory budget (ProcessingMemoryBudgetMb)."
            : "Choose Automatic for the depth memory budget (DepthMemoryBudgetMb), or increase its manual limit.";
        throw new EchoException($"Depth needs an estimated {(minimumBytes + 1048575) / 1048576:N0} MiB for one image with one worker at the requested quality, but {memoryBudgetBytes / 1048576:N0} MiB is available. {advice} Resume to reuse completed checkpoints; image resolution was not reduced.", 5);
    }
}
