using Echo.Models;
using Echo.Infrastructure;
using Echo.Pipeline;

namespace Echo.Tests;

public sealed class FeatureSetCacheTests
{
    private readonly IJsonCodec json;
    private readonly IFileOperations files;

    public FeatureSetCacheTests(IJsonCodec json, IFileOperations files)
    {
        this.json = json;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Feature cache shares concurrent reads and distinguishes artifact versions", () => WithFiles(paths =>
        {
            var counting = new CountingFeatureJsonCodec(json);
            using var cache = new FeatureSetCache(counting, 4096);
            var loaded = new FeatureSet[16];
            Parallel.For(0, loaded.Length, i => loaded[i] = cache.Read(paths[0], default));
            Check(counting.ReadCount(paths[0]) == 1 && loaded.All(value => ReferenceEquals(value, loaded[0])), "Decode a shared artifact once");
            var other = cache.Read(paths[1], default);
            Check(!ReferenceEquals(other, loaded[0]) && counting.ReadCount(paths[1]) == 1, "Different paths with the same image ID are separate versions");
        }));
        tests.Run("Feature cache evicts least recently used values and bypasses oversized inputs", () => WithFiles(paths =>
        {
            var counting = new CountingFeatureJsonCodec(json);
            // Each small fixture occupies just over 512 estimated bytes: two fit.
            using var cache = new FeatureSetCache(counting, 1100);
            cache.Read(paths[0], default);
            cache.Read(paths[1], default);
            cache.Read(paths[0], default);
            cache.Read(paths[2], default);
            cache.Read(paths[0], default);
            cache.Read(paths[1], default);
            Check(counting.ReadCount(paths[0]) == 1 && counting.ReadCount(paths[1]) == 2, "Retain the recently used input and reload the evicted input");
            using var bypass = new FeatureSetCache(counting, 1);
            bypass.Read(paths[2], default);
            bypass.Read(paths[2], default);
            Check(counting.ReadCount(paths[2]) == 3, "Oversized inputs must not remain cached");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                cache.Read(paths[2], cancelled.Token);
                throw new InvalidOperationException("Expected cancellation");
            }
            catch (OperationCanceledException)
            {
            }
            Check(counting.ReadCount(paths[2]) == 3, "Cancelled reads must not perform I/O");
        }));
    }

    private void WithFiles(Action<string[]> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-feature-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"{i}.json")).ToArray();
            foreach (var path in paths)
            {
                files.AtomicJson(path, new FeatureSet { ImageId = "same-image" });
            }
            action(paths);
        }
        finally
        {
            Directory.Delete(root, true);
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
