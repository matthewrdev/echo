using Echo.Contracts;

namespace Echo.DenseCloud;

// A deterministic, bounded preview built as points arrive. Sampling the entire
// disk-backed cloud every few seconds would evict the working depth-map pages.
public sealed class CloudPreviewSampler
{
    private readonly List<DenseSample> samples;
    private readonly int capacity;
    private long seen;
    private long stride = 1;

    public CloudPreviewSampler(int capacity = 40000)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
        samples = new List<DenseSample>(capacity);
    }

    public void Add(DenseSample sample)
    {
        var index = seen++;
        if (index % stride != 0)
        {
            return;
        }
        if (samples.Count == capacity)
        {
            var retained = 0;
            for (var source = 0; source < samples.Count; source += 2)
            {
                samples[retained++] = samples[source];
            }
            samples.RemoveRange(retained, samples.Count - retained);
            stride *= 2;
        }
        if (index % stride == 0)
        {
            samples.Add(sample);
        }
    }

    public DenseCloudReport Report(DenseCloudData cloud)
    {
        var report = new DenseCloudReport
        {
            InputSamples = cloud.InputSamples,
            RetainedSamples = cloud.Samples.Count,
            ImageIds = cloud.ImageIds
        };
        foreach (var point in samples)
        {
            report.Preview.Add(new CloudPoint
            {
                Position = [point.X, point.Y, point.Z],
                ImageA = cloud.ImageIds[point.View],
                Error = 1 - point.Confidence,
                Colour = [point.Red, point.Green, point.Blue]
            });
        }
        return report;
    }
}
