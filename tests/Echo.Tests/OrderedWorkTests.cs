using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Pipeline;

namespace Echo.Tests;

[Export]
public sealed class OrderedWorkTests
{
    private readonly IOrderedWorkExecutor executor;
    private readonly IImageWorkPlanner planner;

    [ImportingConstructor]
    public OrderedWorkTests(IOrderedWorkExecutor executor, IImageWorkPlanner planner)
    {
        this.executor = executor;
        this.planner = planner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Bounded workers execute concurrently and publish on the coordinator in input order", () =>
        {
            using var secondStarted = new ManualResetEventSlim();
            var caller = Environment.CurrentManagedThreadId;
            var published = new List<int>();
            var active = 0;
            executor.Run(7, 2, index =>
            {
                Check(Interlocked.Increment(ref active) <= 2, "Concurrency exceeds the plan");
                try
                {
                    if (index == 0)
                    {
                        Check(secondStarted.Wait(TimeSpan.FromSeconds(10)), "Second worker did not start concurrently");
                    }
                    else if (index == 1)
                    {
                        secondStarted.Set();
                    }
                    return index;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }, (index, result) =>
            {
                Check(Environment.CurrentManagedThreadId == caller && active == 0, "Only the coordinator publishes after joining workers");
                Check(index == result, "Result must stay attached to its input");
                published.Add(result);
            }, default);
            Check(published.SequenceEqual(Enumerable.Range(0, 7)), "Publication order changed");
        });
        tests.Run("Worker failure preserves the original exception and joins all workers", () =>
        {
            var active = 0;
            var published = 0;
            try
            {
                executor.Run(5, 2, index =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        if (index == 0)
                        {
                            throw new EchoException("checkpoint failed", 5);
                        }
                        return index;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }, (_, _) => published++, default);
                throw new InvalidOperationException("Expected worker failure");
            }
            catch (EchoException error)
            {
                Check(error.ExitCode == 5, "Preserve needs-input classification");
            }
            Check(active == 0 && published == 0, "Failed wave must join all workers without publishing an incomplete index");
        });
        tests.Run("Cancellation stops later work and leaves no workers running", () =>
        {
            using var cancelled = new CancellationTokenSource();
            var executed = 0;
            var published = 0;
            try
            {
                executor.Run(10, 2, index =>
                {
                    Interlocked.Increment(ref executed);
                    return index;
                }, (_, _) =>
                {
                    published++;
                    cancelled.Cancel();
                }, cancelled.Token);
                throw new InvalidOperationException("Expected cancellation");
            }
            catch (OperationCanceledException)
            {
            }
            Check(executed == 2 && published == 1, "Do not start the next wave or publish after cancellation");
        });
        tests.Run("Image work plans respect CPU nesting, unit counts and large image memory", () =>
        {
            var images = Enumerable.Range(0, 20).Select(_ => new Photo { Width = 128, Height = 128 }).ToArray();
            var settings = new Settings { FeatureWidth = 128 };
            var features = planner.Features(settings, images);
            var cpuBound = Math.Max(1, Math.Max(1, Environment.ProcessorCount - 2) / Math.Min(2, Environment.ProcessorCount));
            Check(features.Workers >= 1 && features.Workers <= cpuBound, "Respect existing point/line parallel branches");
            Check(planner.Features(settings, images.Take(1).ToArray()).Workers == 1, "Do not schedule more workers than images");
            settings.FeatureWidth = 8192;
            var large = planner.Features(settings, [new Photo { Width = 8192, Height = 8192 }, new Photo { Width = 8192, Height = 8192 }]);
            Check(large.Workers == 1, "Oversized jobs must run alone");
            Check(planner.Features(settings, [new Photo(), new Photo()]).Workers == 1, "Unknown source dimensions must run alone");
            var matching = planner.Matching(settings, 3);
            Check(matching.Workers is >= 1 and <= 3 && matching.FeatureCacheBytes <= 64L * 1024 * 1024, "Bound matching workers and retained cache");
        });
        tests.Run("Cancellation inside a worker joins the wave without publishing partial results", () =>
        {
            using var cancelled = new CancellationTokenSource();
            var active = 0;
            var published = 0;
            try
            {
                executor.Run(6, 2, index =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        cancelled.Cancel();
                        cancelled.Token.ThrowIfCancellationRequested();
                        return index;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }, (_, _) => published++, cancelled.Token);
                throw new InvalidOperationException("Expected cancellation");
            }
            catch (OperationCanceledException)
            {
            }
            Check(active == 0 && published == 0, "Cancellation must join all workers before returning");
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
