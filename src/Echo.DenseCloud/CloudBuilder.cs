using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
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
    private readonly IRasterImageService rasterImageService;
    private readonly ICloudExecutionPlanner executionPlanner;

    [ImportingConstructor]
    public CloudBuilder(IRasterImageService rasterImageService, ICloudExecutionPlanner executionPlanner)
    {
        this.rasterImageService = rasterImageService;
        this.executionPlanner = executionPlanner;
    }

    public DenseCloudData Build(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        CancellationToken cancellation,
        Action<DenseCloudProgress>? progress = null,
        Action<DenseCloudReport>? preview = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var photos = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        foreach (var photo in project.Images)
        {
            photos.Add(photo.Id, photo);
        }
        var cameras = new Dictionary<string, CameraPose>(scene.Cameras.Count, StringComparer.Ordinal);
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        var views = new DepthView[maps.Count];
        var cloud = new DenseCloudData();
        cloud.ImageIds.EnsureCapacity(maps.Count);
        var totalRows = 0;
        var maximumWidth = 1;
        var maximumHeight = 1;
        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];
            views[index] = new DepthView(photos[map.ImageId], cameras[map.ImageId], map);
            cloud.ImageIds.Add(map.ImageId);
            totalRows += map.Height;
            maximumWidth = Math.Max(maximumWidth, map.Width);
            maximumHeight = Math.Max(maximumHeight, map.Height);
        }
        var neighbours = new DepthView[views.Length][];
        for (var index = 0; index < views.Length; index++)
        {
            neighbours[index] = SelectNeighbours(views, index, project.Settings.CloudNeighbours);
        }
        var plan = executionPlanner.Plan(project.Settings, maximumWidth, maximumHeight);
        var batches = new CloudRowBatch[plan.Workers];
        var status = new CloudBuildProgress(views.Length, totalRows, progress, preview, Report);
        var options = new ParallelOptions { MaxDegreeOfParallelism = plan.Workers, CancellationToken = cancellation };
        try
        {
            for (var slot = 0; slot < batches.Length; slot++)
            {
                batches[slot] = new CloudRowBatch(maximumWidth * plan.RowsPerBatch);
            }
            for (var index = 0; index < views.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var view = views[index];
                using var original = SKBitmap.Decode(store.PathFor(photos[view.Map.ImageId].Source));
                var colours = rasterImageService.ResizePixels(original, view.Map.Width);
                if (colours.Width != view.Map.Width || colours.Height != view.Map.Height)
                {
                    throw new EchoException($"Depth-map dimensions do not match the source photograph: {photos[view.Map.ImageId].Name}.", 5);
                }
                ProcessImage(view, neighbours[index], index, colours.Pixels, project.Settings, plan, batches, cloud, status, options);
            }
        }
        finally
        {
            foreach (var batch in batches)
            {
                batch?.Dispose();
            }
        }

        if (cloud.Samples.Count < 30)
        {
            throw new EchoException("Fewer than 30 depth samples agree between views. Inspect alignment and depth before meshing.", 5);
        }
        return cloud;
    }

    private DepthView[] SelectNeighbours(DepthView[] views, int imageIndex, int limit)
    {
        var count = Math.Min(limit, Math.Max(0, views.Length - 1));
        var result = new DepthView[count];
        var distances = new double[count];
        var filled = 0;
        for (var candidate = 0; candidate < views.Length; candidate++)
        {
            if (candidate == imageIndex)
            {
                continue;
            }
            var distance = (views[candidate].Centre - views[imageIndex].Centre).Length;
            var position = filled;
            while (position > 0 && distance.CompareTo(distances[position - 1]) < 0)
            {
                position--;
            }
            if (position >= count)
            {
                continue;
            }
            for (var move = Math.Min(filled, count - 1); move > position; move--)
            {
                distances[move] = distances[move - 1];
                result[move] = result[move - 1];
            }
            distances[position] = distance;
            result[position] = views[candidate];
            filled = Math.Min(count, filled + 1);
        }
        return result;
    }

    private void ProcessImage(DepthView view, DepthView[] neighbours, int imageIndex, SKColor[] colours,
        Settings settings, CloudExecutionPlan plan, CloudRowBatch[] batches, DenseCloudData cloud,
        CloudBuildProgress status, ParallelOptions options)
    {
        var waveRows = plan.Workers * plan.RowsPerBatch;
        for (var firstRow = 0; firstRow < view.Map.Height; firstRow += waveRows)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var activeBatches = Math.Min(plan.Workers, (view.Map.Height - firstRow + plan.RowsPerBatch - 1) / plan.RowsPerBatch);
            Parallel.For(0, activeBatches, options, slot =>
            {
                var start = firstRow + slot * plan.RowsPerBatch;
                var end = Math.Min(view.Map.Height, start + plan.RowsPerBatch);
                ProcessRows(view, neighbours, imageIndex, colours, settings, start, end, batches[slot], options.CancellationToken);
            });
            options.CancellationToken.ThrowIfCancellationRequested();
            var completedRows = 0;
            for (var slot = 0; slot < activeBatches; slot++)
            {
                var batch = batches[slot];
                var offset = cloud.Samples.Count;
                cloud.Samples.EnsureCapacity(offset + batch.Count);
                CollectionsMarshal.SetCount(cloud.Samples, offset + batch.Count);
                batch.Samples.AsSpan(0, batch.Count).CopyTo(CollectionsMarshal.AsSpan(cloud.Samples).Slice(offset));
                cloud.InputSamples += batch.InputSamples;
                completedRows += batch.Rows;
            }
            // Only the caller merges samples and invokes callbacks. Workers never
            // mutate the cloud, so previews observe complete, ordered batches.
            var finished = firstRow + completedRows == view.Map.Height;
            status.Advance(view.Map.ImageId, imageIndex, completedRows, finished, cloud);
        }
    }

    private void ProcessRows(DepthView view, DepthView[] neighbours, int imageIndex, SKColor[] colours,
        Settings settings, int firstRow, int lastRow, CloudRowBatch batch, CancellationToken cancellation)
    {
        var accepted = 0;
        var input = 0;
        Span<DenseSample> samples = batch.Samples;
        var minimumSine = Math.Sin(settings.CloudMinimumParallaxDegrees * Math.PI / 180);
        var minimumSineSquared = minimumSine * minimumSine;
        for (var y = firstRow; y < lastRow; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            var offset = y * view.Map.Width;
            ReadOnlySpan<float> depths = view.Map.Depth.AsSpan(offset, view.Map.Width);
            ReadOnlySpan<SKColor> rowColours = colours.AsSpan(offset, view.Map.Width);
            for (var x = 0; x < depths.Length; x++)
            {
                var depth = depths[x];
                if (!(depth > 0) || !float.IsFinite(depth))
                {
                    continue;
                }
                input++;
                var point = view.Point(x, y, depth);
                var referenceRay = point - view.Centre;
                var referenceLengthSquared = referenceRay.Dot(referenceRay);
                var support = 0;
                foreach (var other in neighbours)
                {
                    var otherRay = point - other.Centre;
                    var cross = referenceRay.Cross(otherRay);
                    var sufficientParallax = cross.Dot(cross) >= minimumSineSquared * referenceLengthSquared * otherRay.Dot(otherRay);
                    if (!sufficientParallax)
                    {
                        continue;
                    }
                    var consistentDepth = other.TrySurfacePoint(point, settings.CloudDepthTolerance, out var surface);
                    if (consistentDepth && view.ReprojectsTo(surface, x, y, settings.CloudReprojectionTolerancePixels))
                    {
                        support++;
                    }
                }
                if (support < settings.CloudMinimumSupport)
                {
                    continue;
                }
                var colour = rowColours[x];
                samples[accepted++] = new DenseSample((float)point.X, (float)point.Y, (float)point.Z,
                    colour.Red, colour.Green, colour.Blue, imageIndex, (float)support / neighbours.Length);
            }
        }
        batch.Count = accepted;
        batch.InputSamples = input;
        batch.Rows = lastRow - firstRow;
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
        cloud.Samples.EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            cloud.Samples.Add(new DenseSample(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadInt32(), reader.ReadSingle()));
        }

        return cloud;
    }
}
