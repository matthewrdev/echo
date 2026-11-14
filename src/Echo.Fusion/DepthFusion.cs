using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Fusion;

[Export(typeof(IDepthFusion)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthFusion : IDepthFusion
{
    [ImportingConstructor]
    public DepthFusion()
    {
    }

    public FusedVolume Fuse(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        DenseCloudData cloud,
        CancellationToken cancellation)
    {
        var photos = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        var cameras = new Dictionary<string, CameraPose>(scene.Cameras.Count, StringComparer.Ordinal);
        foreach (var photo in project.Images)
        {
            photos.Add(photo.Id, photo);
        }
        foreach (var camera in scene.Cameras)
        {
            cameras.Add(camera.ImageId, camera);
        }
        var views = new DepthView[maps.Count];
        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];
            views[index] = new DepthView(photos[map.ImageId], cameras[map.ImageId], map);
        }
        var depthCount = 0;
        foreach (var view in views)
        {
            foreach (var depth in view.Map.Depth)
            {
                if (depth > 0)
                {
                    depthCount++;
                }
            }
        }
        var footprints = new double[depthCount];
        var sampleIndex = 0;
        foreach (var view in views)
        {
            foreach (var depth in view.Map.Depth)
            {
                if (depth > 0)
                {
                    footprints[sampleIndex++] = depth / view.Focal;
                }
            }
        }
        footprints.AsSpan().Sort();
        if (footprints.Length == 0)
        {
            throw new EchoException("No valid depth samples to fuse.", 5);
        }

        var detail = project.Settings.FusionDetailMultiplier;
        var voxel = footprints[footprints.Length / 2] * .8 / Math.Sqrt(detail);
        var cellBudget = Math.Max(20000, project.Settings.FaceLimit * 1.35) * detail;
        var occupied = new HashSet<Grid>();
        // Surface nets emit roughly two faces per occupied surface cell. Choose an
        // evidence-based grid near the requested budget before expensive extraction.
        // This never inserts points or subdivides a coarse mesh to inflate its count.
        for (var pass = 0; pass < 4; pass++)
        {
            occupied.Clear();
            foreach (var point in cloud.Samples)
            {
                occupied.Add(new Grid((int)Math.Floor(point.X / voxel), (int)Math.Floor(point.Y / voxel), (int)Math.Floor(point.Z / voxel)));
            }

            if (occupied.Count * 2 <= cellBudget || pass == 3)
            {
                break;
            }

            voxel *= Math.Sqrt(occupied.Count * 2.0 / cellBudget);
        }

        var result = new FusedVolume
        {
            VoxelSize = voxel,
            InputSamples = cloud.InputSamples,
            ConsistentSamples = cloud.Samples.Count
        };
        var candidates = new HashSet<Grid>();
        foreach (var cell in occupied)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var z = -1; z <= 2; z++)
            {
                for (var y = -1; y <= 2; y++)
                {
                    for (var x = -1; x <= 2; x++)
                    {
                        candidates.Add(new Grid(cell.X + x, cell.Y + y, cell.Z + z));
                    }
                }
            }
        }

        var keys = new Grid[candidates.Count];
        candidates.CopyTo(keys);
        Array.Sort(keys, (a, b) =>
        {
            var order = a.X.CompareTo(b.X);
            if (order == 0)
            {
                order = a.Y.CompareTo(b.Y);
            }
            return order != 0 ? order : a.Z.CompareTo(b.Z);
        });
        var samples = new VolumeSample?[keys.Length];
        var truncation = voxel * 3;
        Parallel.For(0, keys.Length, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, index =>
                {
                    var key = keys[index];
                    var world = new V3(key.X * voxel, key.Y * voxel, key.Z * voxel);
                    double sum = 0;
                    double weight = 0;
                    var count = 0;
                    foreach (var view in views)
                    {
                        var distance = view.Difference(world);
                        if (!double.IsFinite(distance) || Math.Abs(distance) > truncation)
                        {
                            continue;
                        }

                        var w = 1 - .5 * Math.Abs(distance) / truncation;
                        sum += distance * w;
                        weight += w;
                        count++;
                    }

                    if (count >= 2)
                    {
                        samples[index] = new VolumeSample
                        {
                            X = key.X,
                            Y = key.Y,
                            Z = key.Z,
                            Distance = (float)(sum / weight),
                            Views = count
                        };
                    }
                });
        var retained = 0;
        foreach (var sample in samples)
        {
            if (sample != null)
            {
                retained++;
            }
        }
        result.Samples.EnsureCapacity(retained);
        foreach (var sample in samples)
        {
            if (sample != null)
            {
                result.Samples.Add(sample);
            }
        }
        if (result.Samples.Count < 100)
        {
            throw new EchoException("Too little jointly observed volume to reconstruct a surface.", 5);
        }

        return result;
    }
}
