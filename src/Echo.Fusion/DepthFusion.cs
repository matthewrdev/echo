using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Storage;
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
        CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null, IFusionChunkStore? chunks = null, Action<OperationProgress>? operationProgress = null)
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
        var grid = FusionGridBuilder.Build(views, cloud, project.Settings, resources, cancellation, progress, work, operationProgress);
        progress?.Invoke($"Fusion resolution: requested {grid.RequestedVoxelSize:G6} m; using {grid.VoxelSize:G6} m across {grid.Keys.Count:N0} grid samples.");
        var voxel = grid.VoxelSize;
        var keys = grid.Keys;
        var result = new FusedVolume
        {
            Samples = work?.List(new VolumeRecordCodec()) ?? [],
            VoxelSize = voxel,
            InputSamples = cloud.InputSamples,
            ConsistentSamples = cloud.Samples.Count,
            Resolution = new FusionResolutionReport
            {
                PixelFootprint = grid.PixelFootprint,
                RequestedVoxelSize = grid.RequestedVoxelSize,
                MemoryBudgetBytes = resources.MemoryBudgetBytes,
                MaximumGridSamples = resources.MaximumGridSamples,
                CandidateSamples = keys.Count,
                MemoryLimited = voxel > grid.RequestedVoxelSize * 1.001
            }
        };
        operationProgress?.Invoke(new OperationProgress("Volume integration", "Integrating observed depths…", 0, keys.Count, "grid samples"));
        const int chunkSize = 16384;
        for (var first = 0; first < keys.Count; first += chunkSize)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(chunkSize, keys.Count - first);
            IList<VolumeSample> Integrate()
            {
                var samples = new VolumeSample?[count];
                Parallel.For(0, count, new ParallelOptions
                {
                    CancellationToken = cancellation,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
                }, index =>
                {
                    var key = keys[first + index];
                    var world = new V3(key.X * voxel, key.Y * voxel, key.Z * voxel);
                    double sum = 0;
                    double weight = 0;
                    var support = 0;
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
                        support++;
                    }

                    if (support >= 2)
                    {
                        samples[index] = new VolumeSample
                        {
                            X = key.X,
                            Y = key.Y,
                            Z = key.Z,
                            Distance = (float)(sum / weight),
                            Views = support
                        };
                    }
                });
                var retainedChunk = new List<VolumeSample>();
                foreach (var sample in samples)
                {
                    if (sample != null)
                    {
                        retainedChunk.Add(sample);
                    }
                }
                return retainedChunk;
            }
            var completed = chunks?.ReadOrBuild(first, count, Integrate) ?? Integrate();
            foreach (var sample in completed)
            {
                result.Samples.Add(sample);
            }
            var message = $"Integrating volume: {first + count:N0}/{keys.Count:N0} samples; {result.Samples.Count:N0} retained.";
            progress?.Invoke(message);
            operationProgress?.Invoke(new OperationProgress("Volume integration", message, first + count, keys.Count, "grid samples"));
        }
        if (result.Samples.Count < 100)
        {
            throw new EchoException("Too little jointly observed volume to reconstruct a surface.", 5);
        }

        return result;
    }
}
