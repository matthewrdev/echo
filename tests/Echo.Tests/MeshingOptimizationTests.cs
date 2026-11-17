using System.ComponentModel.Composition;
using System.Security.Cryptography;
using Echo.Contracts;
using Echo.Compute;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Simplification;
using Echo.Storage;

namespace Echo.Tests;

[Export]
public sealed class MeshingOptimizationTests
{
    private readonly IBulkArtifactStore artifacts;
    private readonly IArtifactWriteObserver writes;

    [ImportingConstructor]
    public MeshingOptimizationTests(IBulkArtifactStore artifacts, IArtifactWriteObserver writes)
    {
        this.artifacts = artifacts;
        this.writes = writes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("New paging files avoid zero reads and retain dirty cross-page writes", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var records = session.Open(Path.Combine(directory, "values.bin"), new IntRecordCodec(), true);
            var expected = Enumerable.Range(0, 100003).ToArray();
            records.AddRange(expected);
            Check(session.Statistics.ReadBytes == 0, "New pages read zero-filled disk space");
            var changed = Enumerable.Repeat(-19, 9000).ToArray();
            records.WriteRange(16000, changed);
            changed.CopyTo(expected, 16000);
            var actual = new int[expected.Length];
            records.ReadRange(0, actual);
            Check(actual.SequenceEqual(expected), "Dirty range or eviction lost records");
            records.Commit();
            var written = session.Statistics.WrittenBytes;
            records.Commit();
            Check(session.Statistics.WrittenBytes == written, "Repeated commit rewrote unchanged headers");
        }));
        tests.Run("Bulk publication preserves payload bytes and write-time checksums", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, Path.Combine(directory, "scratch"));
            var mesh = work.Surface();
            for (var index = 0; index < 6007; index++)
            {
                mesh.Vertices.Add([index * Math.PI, -0d, double.Epsilon]);
                mesh.Faces.Add([index, index, index]);
                mesh.SourceImages.Add(index % 2 == 0 ? "second" : "first");
            }
            mesh.Vertices[3] = [-19, 17, 99];
            using var scope = writes.BeginScope();
            var path = Path.Combine(directory, "mesh.json");
            artifacts.Write(path, mesh);
            using var saved = artifacts.OpenSession(65536, 0);
            var actual = saved.Read<Surface>(path);
            for (var index = 0; index < mesh.Vertices.Count; index++)
            {
                Check(actual.Vertices[index].SequenceEqual(mesh.Vertices[index]) && actual.Faces[index].SequenceEqual(mesh.Faces[index]), "Bulk copy changed geometry");
                Check(actual.SourceImages[index] == mesh.SourceImages[index], "Bulk copy reused a different string table");
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                Check(scope.TryGetChecksum(file, out var checksum), "Published artifact lacks a write-time checksum");
                Check(checksum == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), "Write-time checksum is incorrect");
            }
            using (var nested = writes.BeginScope())
            {
                var other = Path.Combine(directory, "nested.json");
                artifacts.Write(other, new Surface());
                Check(nested.TryGetChecksum(other, out _) && !scope.TryGetChecksum(other, out _), "Checksum scopes leaked");
            }
            File.AppendAllText(path, " ");
            Check(!scope.TryGetChecksum(path, out _), "Modified file retained a stale checksum");
        }));
    }

    private void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-optimization-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
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
