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
        tests.Run("Paired SIMD quadrics preserve scalar accumulation bits", () =>
        {
            var random = new Random(731);
            var points = new V3[3];
            var errors = new double[3];
            for (var iteration = 0; iteration < 4096; iteration++)
            {
                var first = new Vertex();
                var second = new Vertex();
                var scale = Math.Pow(10, iteration % 51 - 25);
                for (var row = 0; row < 4; row++)
                {
                    for (var column = row; column < 4; column++)
                    {
                        first[row * 4 + column] = (random.NextDouble() - .5) * scale;
                        second[row * 4 + column] = (random.NextDouble() - .5) * scale;
                    }
                }
                for (var point = 0; point < points.Length; point++)
                {
                    points[point] = new V3(random.NextDouble() - .5, random.NextDouble() - .5, random.NextDouble() - .5);
                }
                if (iteration % 7 == 0)
                {
                    points[0] = new V3(-0d, double.Epsilon, 0);
                }
                new EdgeQuadric(first, second).Evaluate(points, errors);
                for (var point = 0; point < points.Length; point++)
                {
                    double[] values = [points[point].X, points[point].Y, points[point].Z, 1];
                    var expected = 0d;
                    for (var row = 0; row < 4; row++)
                    {
                        for (var column = 0; column < 4; column++)
                        {
                            expected += values[row] * values[column] * (first[row * 4 + column] + second[row * 4 + column]);
                        }
                    }
                    Check(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(errors[point]), "SIMD changed a collapse error");
                }
            }
        });
        tests.Run("Incremental triangle bounds match exhaustive intersections on paged and resident storage", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var index = new DynamicTriangleIndex(work);
            var triangles = new List<SurfaceTriangle>();
            var random = new Random(91);
            V3 Point() => new(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble() * 10);
            for (var step = 0; step < 500; step++)
            {
                var centre = new V3(step * .05, step % 5, 0);
                var triangle = new SurfaceTriangle(centre, centre + new V3(.5, 0, .3), centre + new V3(0, .5, -.3));
                index.Add(triangle);
                triangles.Add(triangle);
                for (var query = 0; query < 3; query++)
                {
                    var test = new SurfaceTriangle(Point(), Point(), Point());
                    Check(index.Intersects(test) == triangles.Any(other => SurfaceTriangleIntersection.Intersects(test, other)), "Incremental bounds missed an intersection");
                }
            }
        }));
        tests.Run("Contiguous adjacency initialization retains mutation and encounter order", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var faces = new List<FaceVertices>();
            var random = new Random(31);
            for (var face = 0; face < 1000; face++)
            {
                faces.Add(new FaceVertices(random.Next(100), random.Next(100), random.Next(100)));
            }
            var expected = new VertexFacesStore(100, null);
            var actual = new VertexFacesStore(100, work);
            for (var face = 0; face < faces.Count; face++)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    expected.Add(faces[face][corner], face);
                }
            }
            actual.Initialize(faces, default);
            for (var mutation = 0; mutation < 3000; mutation++)
            {
                var vertex = random.Next(100);
                var face = random.Next(1000);
                if (mutation % 2 == 0)
                {
                    Check(actual.Remove(vertex, face) == expected.Remove(vertex, face), "Adjacency removal changed");
                }
                else
                {
                    actual.Add(vertex, face);
                    expected.Add(vertex, face);
                }
                Check(actual.Faces(vertex).ToArray().SequenceEqual(expected.Faces(vertex).ToArray()), "Adjacency order changed");
            }
        }));
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
        tests.Run("Incremental ear clipping preserves the original triangulation order", () =>
        {
            var random = new Random(51);
            for (var count = 4; count < 130; count += 3)
            {
                var points = new V3[count];
                for (var index = 0; index < count; index++)
                {
                    var radius = 1 + random.NextDouble();
                    var angle = index * 2 * Math.PI / count;
                    points[index] = new V3(radius * Math.Cos(angle), radius * Math.Sin(angle), 0);
                }
                var boundary = Enumerable.Range(0, count).ToList();
                var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(count, new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec());
                var actual = PlanarEarTriangulation.Fill(boundary, points, new V3(0, 0, 1), edges, default);
                var expected = ReferenceEars(points);
                Check(actual != null && actual.Count == expected.Count, "Ear count changed");
                Check(actual!.SelectMany(face => face).SequenceEqual(expected.SelectMany(face => face)), "Ear encounter order changed");
            }
        });
    }

    private List<int[]> ReferenceEars(V3[] points)
    {
        var remaining = Enumerable.Range(0, points.Length).ToList();
        var result = new List<int[]>();
        var normal = new V3(0, 0, 1);
        while (remaining.Count > 3)
        {
            var found = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var before = remaining[(index + remaining.Count - 1) % remaining.Count];
                var current = remaining[index];
                var after = remaining[(index + 1) % remaining.Count];
                var a = points[before];
                var b = points[current];
                var c = points[after];
                if ((b - a).Cross(c - a).Dot(normal) <= 1e-12)
                {
                    continue;
                }
                var blocked = remaining.Any(vertex => vertex != before && vertex != current && vertex != after
                    && (b - a).Cross(points[vertex] - a).Dot(normal) >= -1e-12
                    && (c - b).Cross(points[vertex] - b).Dot(normal) >= -1e-12
                    && (a - c).Cross(points[vertex] - c).Dot(normal) >= -1e-12);
                if (blocked)
                {
                    continue;
                }
                result.Add([before, current, after]);
                remaining.RemoveAt(index);
                found = true;
                break;
            }
            Check(found, "Invalid reference polygon");
        }
        result.Add(remaining.ToArray());
        return result;
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
