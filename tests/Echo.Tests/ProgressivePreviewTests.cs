using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export]
public sealed class ProgressivePreviewTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public ProgressivePreviewTests(IProjectStoreFactory stores, IRunContextFactory contexts, IFileOperations files, IJsonCodec json)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Sparse previews sample the whole scene without changing reconstruction", () =>
        {
            var scene = new ReconstructionScene
            {
                Points = Enumerable.Range(0, 100000).Select(index => new CloudPoint { Position = [index, 0, 0] }).ToList()
            };
            var preview = AlignmentPreviewSampler.Sample(scene);
            Check(preview.TotalPoints == 100000 && preview.Scene.Points.Count == 30000, "Sparse preview is not bounded");
            Check(preview.Scene.Points[^1].Position[0] > 99990, "Sampling only showed the beginning of the cloud");
            Check(scene.Points.Count == 100000, "Preview changed reconstruction output");
        });
        tests.Run("Fusion previews keep bounded representative samples through repeated compaction", () =>
        {
            var sampler = new FusionPreviewSampler(100);
            var volume = new FusedVolume { VoxelSize = .5 };
            for (var index = 0; index < 10001; index++)
            {
                var sample = new VolumeSample { X = index };
                volume.Samples.Add(sample);
                sampler.Add(sample);
            }
            var preview = sampler.Snapshot(volume);
            Check(preview.Volume.Samples.Count <= 100 && preview.TotalSamples == 10001, "Fusion preview grew without bound");
            Check(preview.Volume.Samples[0].X == 0 && preview.Volume.Samples[^1].X > 9800, "Preview lost spatial coverage of completed work");
            Check(preview.Volume.Samples.Select(sample => sample.X).Distinct().Count() == preview.Volume.Samples.Count, "Compaction duplicated samples");
        });
        tests.Run("Progressive snapshots alternate atomically without adding artifact checkpoints", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "echo-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = stores.Open(path);
                store.Init();
                var run = new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = store.Load() };
                using var context = contexts.Create(store, run, default, null);
                var publisher = new RunPreviewPublisher(context, files, RunPreviewStage.Alignment);
                Check(publisher.IsDue, "First preview should appear immediately");
                for (var index = 0; index < 5; index++)
                {
                    publisher.Publish(new AlignmentPreview { TotalPoints = index });
                    var preview = json.Read<AlignmentPreview>(store.PathFor(run.Artifacts["alignmentPreview"] + "/preview.json"));
                    Check(preview.TotalPoints == index, "Published reference does not match saved snapshot");
                }
                Check(!publisher.IsDue, "Preview writes should be throttled");
                Check(Directory.GetFiles(store.PathFor($"runs/{run.Id}/previews"), "*", SearchOption.AllDirectories).Length == 2,
                    "Preview files accumulated across updates");
                Check(Directory.GetFileSystemEntries(store.PathFor("artifacts")).Length == 0, "Live previews polluted completed checkpoints");
            }
            finally
            {
                Directory.Delete(path, true);
            }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
