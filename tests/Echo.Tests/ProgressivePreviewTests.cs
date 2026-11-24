using System.Diagnostics;
using System.Text.Json;
using System.Text;
using Echo.Fusion;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Alignment;
using Echo.Pipeline.Runs;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Tests;

public sealed class ProgressivePreviewTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IAlignmentPreviewWriter alignmentPreviews;

    public ProgressivePreviewTests(IProjectStoreFactory stores, IRunContextFactory contexts, IFileOperations files, IJsonCodec json,
        IAlignmentPreviewWriter alignmentPreviews)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
        this.alignmentPreviews = alignmentPreviews;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Sparse previews sample the whole scene without changing reconstruction", () =>
        {
            var scene = new ReconstructionScene
            {
                Points = Enumerable.Range(0, 100000).Select(index => new CloudPoint { Position = [index, 0, 0] }).ToList()
            };
            var preview = AlignmentPreviewSampler.Sample(scene, 30000);
            Check(preview.TotalPoints == 100000 && preview.Scene.Points.Count == 30000, "Sparse preview is not bounded");
            Check(preview.Scene.Points[^1].Position[0] > 99990, "Sampling only showed the beginning of the cloud");
            Check(scene.Points.Count == 100000, "Preview changed reconstruction output");
            Check(AlignmentPreviewSampler.Sample(scene).Scene.Points.Count == scene.Points.Count,
                "New live previews should include every point below the display limit");
        });
        tests.Run("Full sparse previews publish atomic binary snapshots with bounded storage", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "echo-sparse-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = stores.Open(path);
                store.Init();
                var run = new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = store.Load() };
                using var context = contexts.Create(store, run, default, null);
                var publisher = new RunPreviewPublisher(context, files, RunPreviewStage.Alignment);
                var scene = new ReconstructionScene
                {
                    Points = Enumerable.Range(0, 1100001).Select(index => new CloudPoint
                    {
                        Position = [index % 1000, index / 1000, 10],
                        ImageA = "first",
                        ImageB = "second",
                        PixelA = [12, 34],
                        PixelB = [56, 78],
                        Error = index % 2 == 0 ? .5 : 2,
                        Colour = [83, 210, 185]
                    }).ToList()
                };
                var preview = AlignmentPreviewSampler.Sample(scene);
                var timer = Stopwatch.StartNew();
                publisher.PublishAlignment(preview, alignmentPreviews);
                timer.Stop();
                var saved = store.PathFor(run.Artifacts["alignmentPreview"] + "/preview.bin");
                using (var input = new BinaryReader(File.OpenRead(saved), Encoding.UTF8))
                {
                    Check(input.ReadInt32() == 0x31505345, "Sparse snapshot format marker");
                    var header = JsonSerializer.Deserialize<AlignmentPreviewHeader>(input.ReadBytes(input.ReadInt32()), json.Options)!;
                    Check(header.TotalPoints == scene.Points.Count && header.Scene.Points.Count == 0, "Header contains inline points or loses the total");
                    Check(input.ReadInt32() == 0x31504345 && input.ReadInt32() == 1 && input.ReadInt32() == 76,
                        "Sparse preview must use the existing lossless point records");
                    Check(input.ReadInt32() == preview.Scene.Points.Count && preview.Scene.Points.Count == 1000000, "Snapshot did not use the bounded sample");
                    var codec = new CloudPointRecordCodec(header.Images);
                    var point = codec.Decode(input.ReadBytes(codec.Size));
                    Check(point.Position.SequenceEqual(scene.Points[0].Position) && point.PixelA.SequenceEqual(new double[] { 12, 34 })
                        && point.ImageB == "second" && point.Colour!.SequenceEqual(new byte[] { 83, 210, 185 }), "Preview lost observation data");
                }

                Check(new FileInfo(saved).Length < scene.Points.Count * 77L, "Preview storage should stay compact");
                var output = Environment.GetEnvironmentVariable("ECHO_TEST_ALIGNMENT_PREVIEW_OUTPUT");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    File.Copy(saved, Path.Combine(output, "preview.bin"), true);
                    Console.WriteLine($"  Sparse preview: {scene.Points.Count:N0} points, {new FileInfo(saved).Length:N0} bytes, {timer.ElapsedMilliseconds} ms publication");
                }

                var original = files.HashFile(saved);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try
                {
                    alignmentPreviews.Write(saved, preview, cancelled.Token);
                    throw new InvalidOperationException("Cancelled preview was published");
                }
                catch (OperationCanceledException)
                {
                }

                Check(files.HashFile(saved) == original, "Cancellation damaged a readable snapshot");
                for (var index = 0; index < 4; index++)
                {
                    publisher.PublishAlignment(AlignmentPreviewSampler.Sample(scene, 100), alignmentPreviews);
                }

                Check(Directory.GetFiles(store.PathFor($"runs/{run.Id}/previews"), "*", SearchOption.AllDirectories).Length == 1,
                    "Binary preview updates accumulated files");
                Check(!publisher.IsDue && Directory.GetFileSystemEntries(store.PathFor("artifacts")).Length == 0,
                    "Preview updates bypass throttling or create reconstruction checkpoints");
            }
            finally
            {
                Directory.Delete(path, true);
            }
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
        tests.Run("Fusion preview span sampling preserves every snapshot across batch and compaction boundaries", () =>
        {
            var source = Enumerable.Range(0, 200003).Select(index => new VolumeSample { X = index }).ToArray();
            var volume = new FusedVolume { Samples = source, VoxelSize = .1 };
            foreach (var limit in new[] { 3, 7, 100, 80000 })
            {
                var individual = new FusionPreviewSampler(limit);
                var batched = new FusionPreviewSampler(limit);
                var random = new Random(17);
                for (var first = 0; first < source.Length;)
                {
                    var count = Math.Min(random.Next(1, 4097), source.Length - first);
                    var values = source.AsSpan(first, count);
                    foreach (var sample in values)
                    {
                        individual.Add(sample);
                    }

                    batched.AddRange([]);
                    batched.AddRange(values);
                    Check(batched.Snapshot(volume).Volume.Samples.SequenceEqual(individual.Snapshot(volume).Volume.Samples),
                        "Skipping discarded inputs changed the preview");
                    first += count;
                }
            }
        });
        tests.Run("Progressive snapshots replace atomically without adding artifact checkpoints", () =>
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
                    publisher.PublishAlignment(new AlignmentPreview { TotalPoints = index }, alignmentPreviews);
                    var preview = new AlignmentPreviewReader().Read(store.PathFor(run.Artifacts["alignmentPreview"] + "/preview.bin"), summary: false);
                    Check(preview.TotalPoints == index, "Published reference does not match saved snapshot");
                }

                Check(!publisher.IsDue, "Preview writes should be throttled");
                Check(Directory.GetFiles(store.PathFor($"runs/{run.Id}/previews"), "*", SearchOption.AllDirectories).Length == 1,
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
