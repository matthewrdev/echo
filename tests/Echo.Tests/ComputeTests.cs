using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Contracts;
using Echo.Matching;

namespace Echo.Tests;

[Export]
public sealed class ComputeTests
{
    private readonly IComputeService compute;

    [ImportingConstructor]
    public ComputeTests(IComputeService compute)
    {
        this.compute = compute;
    }

    internal void Run(TestSuite tests, bool requireMetal)
    {
        using (var probe = compute.Open(requireMetal ? ComputeBackend.Metal : ComputeBackend.Auto, default))
        {
            if (probe == null)
            {
                Console.WriteLine("SKIP compute parity: Metal is unavailable");
                return;
            }
            Console.WriteLine("  Compute device: " + probe.Device);
        }
        tests.Run("Metal descriptor blocks preserve mutual, ratio, reversed and tied matches", () =>
        {
            var random = new Random(271);
            var left = new Feature[137];
            var right = new Feature[149];
            for (var i = 0; i < left.Length; i++)
            {
                left[i] = new Feature { Descriptor = Enumerable.Range(0, 128).Select(_ => (float)random.NextDouble() * .1f).ToArray() };
            }
            for (var i = 0; i < right.Length; i++)
            {
                right[i] = new Feature { Descriptor = left[i % left.Length].Descriptor.Select(value => value + .0001f).ToArray() };
            }
            right[9].Descriptor = right[8].Descriptor.ToArray();
            right[26].Descriptor = left[26].Descriptor.Reverse().ToArray();
            foreach (var reversed in new[] { false, true })
            {
                var cpu = DescriptorMatcher.Match<Feature>(left, right, new Settings { ComputeBackend = ComputeBackend.Cpu }, default, reversed);
                var gpu = DescriptorMatcher.Match<Feature>(left, right, new Settings { ComputeBackend = ComputeBackend.Metal }, default, reversed, compute);
                Check(cpu.Matches.Count == gpu.Matches.Count, "Match counts differ");
                for (var i = 0; i < cpu.Matches.Count; i++)
                {
                    Check(cpu.Matches[i] == gpu.Matches[i], "Ordered correspondence or score differs");
                }
            }
        });
        tests.Run("Compute scopes enforce native budgets, restore parents and record GPU use", () =>
        {
            using var outer = compute.BeginScope(1024 * 1024);
            using (var inner = compute.BeginScope(16))
            {
                using var limited = compute.Open(ComputeBackend.Metal, default)!;
                limited.Allocate(0, 16);
                limited.UploadRange<int>(0, 2, [17, 23]);
                var read = new int[4];
                limited.Read<int>(0, read);
                Check(read[2] == 17 && read[3] == 23, "Ranged upload lost its offset");
                try
                {
                    limited.UploadRange<int>(0, 3, [1, 2]);
                    throw new InvalidOperationException("Out-of-bounds upload succeeded");
                }
                catch (ComputeException)
                {
                }
                try
                {
                    limited.Allocate(1, 4);
                    throw new InvalidOperationException("Native budget was exceeded");
                }
                catch (ComputeException)
                {
                }
                Check(compute.MemoryBudgetBytes == 16, "Inner budget was not applied");
            }
            Check(compute.MemoryBudgetBytes == 1024 * 1024, "Parent budget was not restored");
            using (var session = compute.Open(ComputeBackend.Metal, default)!)
            {
                session.Upload<float>(0, new float[128 * 128]);
                session.Upload<float>(1, new float[128 * 128]);
                session.Upload<float>(2, new float[128 * 128]);
                session.Allocate(3, 128 * 128 * 4);
                session.Dispatch("depth_filter", 128 * 128, [128, 128]);
                session.Read<int>(3, new int[128 * 128]);
            }
            Check(outer.Devices.Length == 1, "Artifact scope did not record GPU work");
        });
        tests.Run("Automatic selection falls back atomically when native memory is exhausted", () =>
        {
            var features = Enumerable.Range(0, 260).Select(i => new Feature { Descriptor = [(float)i, .1f, .2f, .3f] }).ToArray();
            var expected = DescriptorMatcher.Match<Feature>(features, features, new Settings { ComputeBackend = ComputeBackend.Cpu }, default, false);
            using var scope = compute.BeginScope(16);
            var actual = DescriptorMatcher.Match<Feature>(features, features, new Settings { ComputeBackend = ComputeBackend.Auto }, default, false, compute);
            Check(expected.Matches.SequenceEqual(actual.Matches), "Automatic fallback changed matches");
            try
            {
                DescriptorMatcher.Match<Feature>(features, features, new Settings { ComputeBackend = ComputeBackend.Metal }, default, false, compute);
                throw new InvalidOperationException("Explicit Metal hid a resource failure");
            }
            catch (ComputeException)
            {
            }
        });
        tests.Run("Compute cancellation releases the session and rejects disposed buffers", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var session = compute.Open(ComputeBackend.Metal, cancellation.Token)!;
            cancellation.Cancel();
            try
            {
                session.Allocate(0, 4);
                throw new InvalidOperationException("Cancelled allocation succeeded");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                session.Dispose();
            }
            try
            {
                session.Allocate(0, 4);
                throw new InvalidOperationException("Disposed session accepted a buffer");
            }
            catch (ObjectDisposedException)
            {
            }
            using var reopened = compute.Open(ComputeBackend.Metal, default);
            Check(reopened != null, "Session lease was not released");
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
