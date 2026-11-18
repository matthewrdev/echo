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
            var left = new Feature[537];
            var right = new Feature[549];
            for (var i = 0; i < left.Length; i++)
            {
                left[i] = new Feature { Descriptor = Enumerable.Range(0, 128).Select(_ => (float)random.NextDouble() * .1f).ToArray() };
            }
            for (var i = 0; i < right.Length; i++)
            {
                right[i] = new Feature { Descriptor = left[i % left.Length].Descriptor.Select(value => value + .0001f).ToArray() };
            }
            right[9].Descriptor = right[8].Descriptor.ToArray();
            right[529].Descriptor = right[8].Descriptor.ToArray();
            left[530].Descriptor = left[17].Descriptor.ToArray();
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
        tests.Run("Mixed point and junction descriptors use Metal while preserving CPU matches and rejection counts", () =>
        {
            var random = new Random(724);
            var left = Enumerable.Range(0, 300).Select(_ => new Feature
            {
                Descriptor = Enumerable.Range(0, 128).Select(_ => (float)random.NextDouble() * .1f).ToArray()
            }).ToList();
            var right = left.Select(feature => new Feature { Descriptor = feature.Descriptor.ToArray() }).ToList();
            left.Insert(0, new Feature { Descriptor = new float[19] });
            left.Add(new Feature { Descriptor = [] });
            left.Add(new Feature { Descriptor = [.1f, .2f, .3f] });
            right.Add(new Feature { Descriptor = [.1f, .2f, .3f] });
            for (var index = 0; index < 5; index++)
            {
                var descriptor = Enumerable.Range(0, 64).Select(_ => (float)random.NextDouble() * .1f).ToArray();
                left.Insert(index * 9, new Feature { Descriptor = descriptor });
                right.Insert(index * 7, new Feature { Descriptor = descriptor.ToArray() });
            }
            right.Add(new Feature { Descriptor = right[20].Descriptor.ToArray() });
            right.Add(new Feature { Descriptor = new float[23] });
            foreach (var reversed in new[] { false, true })
            {
                var first = left.ToArray();
                var second = right.ToArray();
                var cpu = DescriptorMatcher.Match<Feature>(first, second, new Settings { ComputeBackend = ComputeBackend.Cpu }, default, reversed);
                using var scope = compute.BeginScope(128L * 1024 * 1024);
                var gpu = DescriptorMatcher.Match<Feature>(first, second, new Settings { ComputeBackend = ComputeBackend.Auto }, default, reversed, compute);
                Check(cpu.Matches.SequenceEqual(gpu.Matches), "Mixed descriptor groups changed ordered correspondences or scores");
                Check(scope.Devices.Length > 0, "Mixed descriptor lengths silently disabled acceleration");
                Check(cpu.Diagnostics.Accepted == gpu.Diagnostics.Accepted, "Accepted count changed");
                Check(cpu.Diagnostics.RejectedAmbiguous == gpu.Diagnostics.RejectedAmbiguous, "Ambiguous count changed");
                Check(cpu.Diagnostics.RejectedNonMutual == gpu.Diagnostics.RejectedNonMutual, "Non-mutual count changed");
                Check(cpu.Diagnostics.RejectedDistance == gpu.Diagnostics.RejectedDistance, "Distance rejection count changed");
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
                session.Dispatch(ComputeKernel.DepthFilter, 128 * 128, [128, 128]);
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
