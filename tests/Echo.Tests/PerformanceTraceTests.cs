using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Telemetry;

namespace Echo.Tests;

[Export]
public sealed class PerformanceTraceTests
{
    private readonly IPerformanceTrace trace;
    private readonly IPerformanceTraceStore traces;
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IComputeService compute;

    [ImportingConstructor]
    public PerformanceTraceTests(IPerformanceTrace trace, IPerformanceTraceStore traces, IProjectStoreFactory stores,
        IRunContextFactory contexts, IFileOperations files, IJsonCodec json, IComputeService compute)
    {
        this.trace = trace;
        this.traces = traces;
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
        this.compute = compute;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Performance scopes preserve parallel parents and independent snapshots", () =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "depth");
            using (trace.Begin("depth", PerformanceSpanKind.Stage, PerformanceBackend.Mixed, "depth"))
            {
                Parallel.For(0, 12, index =>
                {
                    using var image = trace.Begin("image", PerformanceSpanKind.Checkpoint);
                    trace.Operation(new OperationProgress("search", "Searching", index, 12));
                    using var kernel = trace.Begin("kernel", PerformanceSpanKind.Compute, PerformanceBackend.Metal);
                    kernel!.Measure(10, 32, 2.5);
                });
                var first = session.Snapshot();
                first.Spans.Clear();
                first.Statistics[0].Count = -1;
                Check(session.Snapshot().Spans.Count > 0, "Snapshots must own their collections");
            }
            session.Finish(PerformanceStatus.Completed);
            var saved = session.Snapshot();
            var byId = saved.Spans.ToDictionary(span => span.Id);
            foreach (var span in saved.Spans.Where(span => span.Name == "kernel"))
            {
                Check(byId[span.ParentId!.Value].Name == "search", "Parallel operation lost its parent");
                Check(span.Status == PerformanceStatus.Completed && span.DurationMilliseconds >= 0, "Invalid duration/status");
            }
            var aggregate = saved.Statistics.Single(value => value.Name == "kernel");
            Check(aggregate.Count == 12 && aggregate.Items == 120 && aggregate.Bytes == 384, "Aggregate lost parallel work");
            Check(aggregate.GpuMilliseconds == 30 && saved.Summary.Status == PerformanceStatus.Completed, "GPU time or terminal status lost");
        });
        tests.Run("Performance details are bounded without losing stage boundaries or totals", () =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "fusion");
            using (trace.Begin("fusion", PerformanceSpanKind.Stage, stage: "fusion"))
            {
                for (var index = 0; index < 20100; index++)
                {
                    using var item = trace.Begin("batch", PerformanceSpanKind.Compute);
                    item!.Measure(1);
                }
            }
            session.Finish(PerformanceStatus.Completed);
            var saved = session.Snapshot();
            Check(saved.Spans.Count == 20001 && saved.Summary.DroppedSpans == 100, "Timeline is not bounded");
            Check(saved.Spans.Any(span => span.Kind == PerformanceSpanKind.Stage), "Stage boundary was dropped");
            Check(saved.Statistics.Single(value => value.Name == "batch").Items == 20100, "Truncation lost aggregate work");
        });
        tests.Run("Saved traces retain separate attempts, reuse and cancellation", () => WithProject(store =>
        {
            var run = new RunData { Id = Guid.NewGuid().ToString("N"), StorageVersion = 3, Target = "features", Snapshot = store.Load() };
            using (var context = contexts.Create(store, run, default, null))
            {
                context.Start();
                context.Build("features", new
                {
                    input = 1
                }, directory => files.AtomicBytes(Path.Combine(directory, "sample"), [1]));
                context.Complete();
            }
            using (var context = contexts.Create(store, run, default, null))
            {
                context.Start();
                context.Build("features", new
                {
                    input = 1
                }, _ => throw new InvalidOperationException("Cache miss"));
                try
                {
                    context.Build("features", new
                    {
                        input = 2
                    }, _ => throw new OperationCanceledException());
                }
                catch (OperationCanceledException)
                {
                    context.Cancel();
                }
            }
            var directory = store.PathFor($"runs/{run.Id}");
            var attempts = traces.List(directory);
            Check(attempts.Count == 2 && attempts.Select(value => value.Id).Distinct().Count() == 2, "Resume overwrote an attempt");
            var saved = traces.Read(directory, attempts[0].Id);
            Check(saved.Summary.Status == PerformanceStatus.Cancelled, "Cancellation was not saved");
            Check(saved.Spans.Any(span => span.Status == PerformanceStatus.Reused), "Reused time is indistinguishable from computation");
            Check(saved.Spans.Any(span => span.Status == PerformanceStatus.Cancelled), "Failed checkpoint did not close");
            Check(json.Encode(saved).Contains("\"cancelled\"", StringComparison.Ordinal), "Enums must use the typed camel-case wire contract");
            try
            {
                traces.Read(directory, "../run");
                throw new InvalidOperationException("Unsafe attempt path accepted");
            }
            catch (EchoException)
            {
            }
        }));
        tests.Run("Worker recovery closes interrupted traces at the last measured duration", () => WithProject(store =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "alignment");
            using var operation = trace.Begin("long solve", PerformanceSpanKind.Operation);
            var snapshot = session.Snapshot();
            var directory = store.PathFor($"runs/{snapshot.Summary.RunId}");
            traces.Save(directory, snapshot);
            traces.RecoverInterrupted(directory, false);
            var recovered = traces.Read(directory, snapshot.Summary.Id);
            Check(recovered.Summary.Status == PerformanceStatus.Interrupted, "Lost worker remained running");
            Check(recovered.Spans.Single().Status == PerformanceStatus.Interrupted, "Unfinished operation remained running");
            Check(recovered.Summary.DurationMilliseconds == snapshot.Summary.DurationMilliseconds, "Recovery invented elapsed time");
        }));
        tests.Run("Metal tracing records real completed batch time without per-dispatch waits", () =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "depth");
            using (var gpu = compute.Open(ComputeBackend.Auto, default))
            {
                if (gpu == null)
                {
                    Console.WriteLine("SKIP GPU timing: no Metal device");
                    return;
                }
                gpu.Upload<float>(0, Enumerable.Repeat(1f, 128 * 128).ToArray());
                gpu.Upload<float>(1, new float[128 * 128]);
                gpu.Upload<float>(2, new float[128 * 128]);
                gpu.Allocate(3, 128 * 128 * 4);
                gpu.Dispatch(ComputeKernel.DepthFilter, 128 * 128, [128, 128]);
                gpu.Read<int>(3, new int[128 * 128]);
            }
            session.Finish(PerformanceStatus.Completed);
            var saved = session.Snapshot();
            Check(saved.Spans.Any(span => span.GpuMilliseconds > 0), "Completed GPU duration was not captured");
            Check(saved.Spans.Any(span => span.Name == nameof(ComputeKernel.DepthFilter)), "Kernel was not named");
            Check(Math.Abs(saved.Statistics.Sum(value => value.GpuMilliseconds ?? 0) - saved.Spans.Sum(value => value.GpuMilliseconds ?? 0)) < .0001,
                "GPU duration is duplicated or lost in aggregation");
        });
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = stores.Open(path);
            store.Init();
            action(store);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
