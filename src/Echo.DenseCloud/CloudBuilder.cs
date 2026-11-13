using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.DenseCloud;

[Export(typeof(ICloudBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class CloudBuilder : ICloudBuilder
{
    private readonly ICameraGeometry cameraGeometry;
    private readonly IRasterImageService rasterImageService;

    [ImportingConstructor]
    public CloudBuilder(ICameraGeometry cameraGeometry, IRasterImageService rasterImageService)
    {
        this.cameraGeometry = cameraGeometry;
        this.rasterImageService = rasterImageService;
    }

    public DenseCloudData Build(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        CancellationToken cancellation,
        Action<DenseCloudProgress>? progress = null,
        Action<DenseCloudReport>? preview = null)
    {
        var views = maps.Select(map => new DepthView(cameraGeometry, project.Images.Single(i => i.Id == map.ImageId), scene.Cameras.Single(c => c.ImageId == map.ImageId), map)).ToArray();
        var cloud = new DenseCloudData
        {
            ImageIds = maps.Select(m => m.ImageId).ToList()
        };
        var totalRows = maps.Sum(map => map.Height);
        var completedRows = 0;
        var progressClock = Stopwatch.StartNew();
        var previewClock = Stopwatch.StartNew();
        for (var index = 0; index < views.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var view = views[index];
            var neighbours = views.Where(v => v != view).OrderBy(v => (v.Centre - view.Centre).Length).Take(project.Settings.CloudNeighbours).ToArray();
            using var original = SKBitmap.Decode(store.PathFor(project.Images.Single(i => i.Id == view.Map.ImageId).Source));
            using var image = rasterImageService.Resize(original, view.Map.Width);
            var colours = image.Pixels;
            for (var y = 0; y < view.Map.Height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < view.Map.Width; x++)
                {
                    var pixel = y * view.Map.Width + x;
                    var depth = view.Map.Depth[pixel];
                    if (depth <= 0)
                    {
                        continue;
                    }

                    cloud.InputSamples++;
                    var point = view.Point(x, y, depth);
                    var support = 0;
                    foreach (var other in neighbours)
                    {
                        if (Math.Abs(other.Difference(point)) < Math.Max(depth / view.Focal * 2, depth * project.Settings.CloudDepthTolerance))
                        {
                            support++;
                        }
                    }

                    if (support < project.Settings.CloudMinimumSupport)
                    {
                        continue;
                    }

                    var colour = colours[pixel];
                    cloud.Samples.Add(new DenseSample((float)point.X, (float)point.Y, (float)point.Z, colour.Red, colour.Green, colour.Blue, index, (float)support / neighbours.Length));
                }

                completedRows++;
                if (progressClock.ElapsedMilliseconds >= 500 || y == view.Map.Height - 1)
                {
                    progress?.Invoke(new DenseCloudProgress
                    {
                        ImageId = view.Map.ImageId,
                        ImageNumber = index + 1,
                        TotalImages = views.Length,
                        CompletedRows = completedRows,
                        TotalRows = totalRows,
                        RetainedPoints = cloud.Samples.Count
                    });
                    progressClock.Restart();
                }

                var shouldPublishPreview = preview != null
                    && cloud.Samples.Count > 0
                    && (previewClock.ElapsedMilliseconds >= 3000 || completedRows == totalRows);
                if (shouldPublishPreview)
                {
                    preview!(Report(cloud));
                    previewClock.Restart();
                }
            }
        }

        if (cloud.Samples.Count < 30)
        {
            throw new EchoException("Fewer than 30 depth samples agree between views. Inspect alignment and depth before meshing.", 5);
        }

        return cloud;
    }

    public DenseCloudReport Report(DenseCloudData cloud)
    {
        var report = new DenseCloudReport
        {
            InputSamples = cloud.InputSamples,
            RetainedSamples = cloud.Samples.Count,
            ImageIds = cloud.ImageIds
        };
        var stride = Math.Max(1, cloud.Samples.Count / 40000);
        for (var i = 0; i < cloud.Samples.Count; i += stride)
        {
            var p = cloud.Samples[i];
            report.Preview.Add(new CloudPoint
            {
                Position = [p.X, p.Y, p.Z],
                ImageA = cloud.ImageIds[p.View],
                Error = 1 - p.Confidence,
                Colour = [p.Red, p.Green, p.Blue]
            });
        }

        return report;
    }

    public void Write(string path, DenseCloudData cloud)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes($"ply\nformat binary_little_endian 1.0\nelement vertex {cloud.Samples.Count}\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"));
        foreach (var point in cloud.Samples)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
            writer.Write(point.Red);
            writer.Write(point.Green);
            writer.Write(point.Blue);
            writer.Write(point.View);
            writer.Write(point.Confidence);
        }
    }

    public DenseCloudData Read(string path, DenseCloudReport report)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var line = new StringBuilder();
        var count = 0;
        while (stream.Position < stream.Length)
        {
            var value = reader.ReadByte();
            if (value != 10)
            {
                line.Append((char)value);
                continue;
            }

            var text = line.ToString();
            line.Clear();
            if (text.StartsWith("element vertex ", StringComparison.Ordinal))
            {
                count = int.Parse(text[15..], CultureInfo.InvariantCulture);
            }

            if (text == "end_header")
            {
                break;
            }
        }

        if (count != report.RetainedSamples || stream.Length - stream.Position != count * 23L)
        {
            throw new EchoException("Invalid dense cloud checkpoint.");
        }

        var cloud = new DenseCloudData
        {
            ImageIds = report.ImageIds,
            InputSamples = report.InputSamples
        };
        for (var i = 0; i < count; i++)
        {
            cloud.Samples.Add(new DenseSample(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadInt32(), reader.ReadSingle()));
        }

        return cloud;
    }
}
