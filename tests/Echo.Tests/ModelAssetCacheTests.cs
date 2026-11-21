using System.Security.Cryptography;
using Echo.Models;
using Echo.SceneSemantics;

namespace Echo.Tests;

public sealed class ModelAssetCacheTests
{
    public ModelAssetCacheTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Scene model is downloaded once and reused offline across projects", () => WithDirectory(root =>
        {
            var content = new byte[] { 1, 2, 3 };
            var asset = Asset(content);
            using var download = new TestModelDownloadHandler(content);
            var cache = new ModelAssetCache(root, download);
            var messages = new List<string>();
            var path = cache.EnsureAvailable(asset, Path.Combine(root, "project-a"), default, messages.Add);
            download.Status = System.Net.HttpStatusCode.ServiceUnavailable;
            var other = new ModelAssetCache(root, download);
            Check(other.EnsureAvailable(asset, Path.Combine(root, "project-b"), default, messages.Add) == path, "Projects share one path");
            Check(download.Requests == 1 && File.ReadAllBytes(path).SequenceEqual(content), "Reuse requires no network request");
            Check(messages.Any(message => message.Contains("no download needed")), "Explain cached model reuse");
        }));
        tests.Run("Existing project model seeds shared cache and repairs corrupt cache offline", () => WithDirectory(root =>
        {
            var content = new byte[] { 4, 5, 6 };
            var asset = Asset(content);
            var legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacy);
            File.WriteAllBytes(Path.Combine(legacy, asset.FileName), content);
            using var download = new TestModelDownloadHandler([]) { Status = System.Net.HttpStatusCode.ServiceUnavailable };
            var cache = new ModelAssetCache(root, download);
            var path = cache.EnsureAvailable(asset, legacy, default);
            File.WriteAllBytes(path, [0]);
            cache.EnsureAvailable(asset, legacy, default);
            Check(download.Requests == 0 && File.ReadAllBytes(path).SequenceEqual(content), "Verified legacy models are reused without downloading");
            Check(File.Exists(Path.Combine(legacy, asset.FileName)), "Retain original project model");
        }));
        tests.Run("Failed, oversized and cancelled model downloads never become cached models", () => WithDirectory(root =>
        {
            var content = new byte[] { 7, 8, 9 };
            var asset = Asset(content);
            using var bad = new TestModelDownloadHandler([0]);
            ExpectFailure(() => new ModelAssetCache(root, bad).EnsureAvailable(asset, root, default));
            using var oversized = new TestModelDownloadHandler(new byte[20]);
            ExpectFailure(() => new ModelAssetCache(root, oversized).EnsureAvailable(asset with { MaximumBytes = 10 }, root, default));
            using var cancellation = new CancellationTokenSource();
            using var cancelled = new TestModelDownloadHandler(content)
            {
                BeforeResponse = token =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            };
            try
            {
                new ModelAssetCache(root, cancelled).EnsureAvailable(asset, root, cancellation.Token);
                throw new InvalidOperationException("Cancellation was ignored");
            }
            catch (OperationCanceledException)
            {
            }

            Check(!Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories).Any(), "No invalid cached model");
            Check(!Directory.EnumerateFiles(root, "*.download", SearchOption.AllDirectories).Any(), "Temporary downloads are cleaned up");
            using var valid = new TestModelDownloadHandler(content);
            Check(File.Exists(new ModelAssetCache(root, valid).EnsureAvailable(asset, root, default)), "Retry succeeds after failure");
        }));
        tests.Run("Concurrent projects share a single model download and lock waiting is cancellable", () => WithDirectory(root =>
        {
            var content = new byte[] { 10, 11, 12 };
            var asset = Asset(content);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var started = new ManualResetEventSlim();
            using var waiting = new ManualResetEventSlim();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var download = new TestModelDownloadHandler(content)
            {
                BeforeResponse = async token =>
                {
                    started.Set();
                    await release.Task.WaitAsync(token);
                }
            };
            var first = Task.Run(() => new ModelAssetCache(root, download).EnsureAvailable(asset, root, cancellation.Token));
            Check(started.Wait(TimeSpan.FromSeconds(3)), "First download starts");
            var second = Task.Run(() => new ModelAssetCache(root, download).EnsureAvailable(asset, root, cancellation.Token,
                message =>
                {
                    if (message.StartsWith("Waiting", StringComparison.Ordinal))
                    {
                        waiting.Set();
                    }
                }));
            try
            {
                Check(waiting.Wait(TimeSpan.FromSeconds(3)), "Second project waits for first download");
                using var stopped = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
                try
                {
                    new ModelAssetCache(root, download).EnsureAvailable(asset, root, stopped.Token);
                    throw new InvalidOperationException("Cache lock ignored cancellation");
                }
                catch (OperationCanceledException)
                {
                }
            }
            finally
            {
                release.TrySetResult();
                Task.WaitAll([first, second], cancellation.Token);
            }

            Check(first.Result == second.Result && download.Requests == 1, "Only one transfer serves both projects");
        }));
    }

    private ModelAsset Asset(byte[] content)
    {
        return new ModelAsset
        {
            Name = "test scene model",
            FileName = "test.onnx",
            Fingerprint = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            DownloadUri = new Uri("https://huggingface.co/test/model.onnx"),
            DownloadSize = "3 bytes",
            MaximumBytes = 100
        };
    }

    private void WithDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-model-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private void ExpectFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Invalid model was accepted");
        }
        catch (EchoException)
        {
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
