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
        FusionResourcePlan resources,
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
        var grid = FusionGridBuilder.Build(views, cloud, project.Settings, resources, cancellation);
        var voxel = grid.VoxelSize;
        var keys = grid.Keys;
        var result = new FusedVolume
        {
            VoxelSize = voxel,
            InputSamples = cloud.InputSamples,
            ConsistentSamples = cloud.Samples.Count,
            Resolution = new FusionResolutionReport
            {
                PixelFootprint = grid.PixelFootprint,
                RequestedVoxelSize = grid.RequestedVoxelSize,
                MemoryBudgetBytes = resources.MemoryBudgetBytes,
                MaximumGridSamples = resources.MaximumGridSamples,
                CandidateSamples = keys.Length,
                MemoryLimited = voxel > grid.RequestedVoxelSize * 1.001
            }
        };
        var samples = new VolumeSample?[keys.Length];
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
                        var observation = view.Observe(world, project.Settings.CloudDepthTolerance);
                        if (!observation.IsValid)
                        {
                            continue;
                        }

                        var distance = observation.Distance;
                        // Spatial sampling and depth uncertainty are different.
                        // Refining the grid must not reject depths that passed the
                        // cloud's relative-depth / two-pixel agreement tolerance.
                        var uncertainty = FusionDepthUncertainty.Radius(observation.CameraDepth, view.Focal, project.Settings.CloudDepthTolerance);
                        var truncation = Math.Max(voxel * 3, uncertainty * 2);
                        // Equal relative error means larger world-space
                        // uncertainty at longer range. Integrate by precision,
                        // so a distant flight pass cannot overwhelm close detail.
                        var precision = 1 / (uncertainty * uncertainty);
                        if (distance < -truncation)
                        {
                            // A surface in front of this voxel occludes it.
                            continue;
                        }

                        if (distance > truncation)
                        {
                            if (observation.HasFullCoverage)
                            {
                                // The observed surface lies farther along this
                                // ray: integrate positive, truncated free space
                                // rather than silently accepting a ghost layer.
                                sum += truncation * .5 * precision;
                                weight += .5 * precision;
                            }
                            continue;
                        }

                        var w = (1 - .5 * Math.Abs(distance) / truncation) * precision;
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
