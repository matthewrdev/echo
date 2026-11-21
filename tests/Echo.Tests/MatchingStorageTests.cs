using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Tests;

public sealed class MatchingStorageTests
{
    private readonly IFeatureArtifactStore features;
    private readonly IMatchArtifactStore matches;
    private readonly IMatchingIndexStore indexes;
    private readonly IFileOperations files;

    public MatchingStorageTests(IFeatureArtifactStore features, IMatchArtifactStore matches, IMatchingIndexStore indexes, IFileOperations files)
    {
        this.features = features;
        this.matches = matches;
        this.indexes = indexes;
        this.files = files;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Binary features retain exact values and contiguous mixed descriptor partitions", () => Temporary(root =>
        {
            var source = new FeatureSet
            {
                ImageId = "photo",
                Features = [
                    new Feature { X = Math.PI, Y = -0.0, Kind = "corner", Descriptor = [float.Epsilon, -.25f, 1, 0] },
                    new Feature { X = 2, Y = 3, Kind = "junction", Descriptor = [.4f, .5f] },
                    new Feature { X = 4, Y = 5, Kind = "corner", Descriptor = [.6f, .7f, .8f, .9f] }
                ],
                Lines = [new LineFeature { Start = [1, 2], End = [3, 4], Strength = Math.PI, Descriptor = [.2f, .3f] }]
            };
            var path = Path.Combine(root, "features.json");
            features.Write(path, source);
            var restored = features.Read(path);
            Check(MemoryMarshal.TryGetArray(restored.Features[0].DescriptorData, out var first), "Expected packed storage");
            Check(MemoryMarshal.TryGetArray(restored.Features[2].DescriptorData, out var second), "Expected packed storage");
            Check(ReferenceEquals(first.Array, second.Array) && second.Offset == first.Offset + 4, "Equal-length descriptors must be contiguous in original order");
            Check(files.Hash(source) == files.Hash(restored), "Binary storage changed values or ordering");
            var metadata = features.Read(path, descriptors: false);
            Check(metadata.Features[0].X == Math.PI && metadata.Features.All(feature => feature.DescriptorData.IsEmpty), "Location-only reads must omit descriptor allocations");
            var payload = Path.Combine(root, features.Header(path).Payload);
            using (var corrupt = new FileStream(payload, FileMode.Open, FileAccess.Write))
            {
                corrupt.SetLength(corrupt.Length - 1);
            }
            Throws(() => features.Read(path));
        }));
        tests.Run("Binary matches preserve manual ties, verification states, line endpoints and scores", () => Temporary(root =>
        {
            var pair = new ImagePair
            {
                A = "left",
                B = "right",
                Diagnostics = new MatchingDiagnostics { Accepted = 3 },
                Verification = new PairVerification { CandidateCount = 3, InlierCount = 1 },
                Matches = [
                    new Match { A = [Math.PI, 2], B = [3, 4], Score = double.Epsilon, GeometricInlier = true },
                    new Match { A = [5, 6], B = [7, 8], Kind = "junction", GeometricInlier = false },
                    new Match { A = [9, 10], B = [11, 12], Manual = true }
                ],
                LineMatches = [new LineMatch { IndexA = 3, IndexB = 8, StartA = [1, 2], EndA = [3, 4], StartB = [5, 6], EndB = [7, 8], Score = Math.PI }]
            };
            var path = Path.Combine(root, "pair.json");
            matches.Write(path, pair);
            Check(files.Hash(pair) == files.Hash(matches.Read(path)), "Binary matching changed persisted decisions");
            var header = matches.Header(path);
            Check(new FileInfo(Path.Combine(root, header.Payload)).Length == 16 + 3 * 48 + 80, "Small records must not be padded to paging blocks");
            File.Delete(Path.Combine(root, header.Payload));
            Check(matches.Header(path).PointCount == 3, "Summary reads must not access payloads");
            File.WriteAllText(path, "{\"a\":\"old\",\"b\":\"old\",\"matches\":[]}");
            Throws(() => matches.Header(path));
            Throws(() => matches.Read(path));
        }));
        tests.Run("Matching journals recover incomplete tails and reject changed run membership", () => Temporary(root =>
        {
            var path = Path.Combine(root, "pairs.ndjson");
            var first = new MatchingIndexEntry(0, "artifacts/match-pair/a", "a", "b", 5, 0);
            using (var journal = indexes.Open(path))
            {
                journal.Append(first);
            }
            File.AppendAllText(path, "{\"sequence\":1");
            Check(indexes.Read(path).Count == 1, "Live readers must ignore uncommitted journal tails");
            using (var journal = indexes.Open(path))
            {
                Check(journal.Count == 1, "Interrupted trailing record must be discarded");
                journal.Append(first);
                journal.Append(new MatchingIndexEntry(1, "artifacts/match-pair/b", "b", "c", 7, 1));
                Throws(() => journal.Append(first with { Artifact = "changed" }));
            }
            Check(indexes.Read(path).Count == 2, "Resume must not duplicate completed entries");
        }));
    }

    private void Temporary(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-matching-storage-" + Guid.NewGuid().ToString("N"));
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

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Throws(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection of invalid persisted data.");
    }
}
