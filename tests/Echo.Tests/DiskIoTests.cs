using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Fusion;
using Echo.Storage;
using Echo.Pipeline;
using System.Buffers.Binary;
using System.Text;

namespace Echo.Tests;

public sealed class DiskIoTests
{
    private readonly IBulkArtifactStore artifacts;
    private readonly ICloudBuilder clouds;
    private readonly IPlyWriter plyWriter;
    private readonly IArtifactWriteObserver writes;

    public DiskIoTests(IBulkArtifactStore artifacts, ICloudBuilder clouds, IPlyWriter plyWriter, IArtifactWriteObserver writes)
    {
        this.artifacts = artifacts;
        this.clouds = clouds;
        this.plyWriter = plyWriter;
        this.writes = writes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Binary mesh PLY preserves float coordinates and triangle order across paged block tails", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            using var work = new WorkBuffers(session, directory);
            var paged = work.Surface();
            var memory = new Surface();
            for (var index = 0; index < 6007; index++)
            {
                double[] point = [index * Math.PI, -0d, float.Epsilon];
                int[] face = [index, (index + 1) % 6007, (index + 2) % 6007];
                memory.Vertices.Add(point);
                paged.Vertices.Add(point);
                memory.Faces.Add(face);
                paged.Faces.Add(face);
            }
            paged.Vertices[3] = memory.Vertices[3] = [-17.25, double.Epsilon, float.MaxValue];
            paged.Faces[4] = memory.Faces[4] = [17, 23, 6006];
            var actualPath = Path.Combine(directory, "actual.ply");
            var expectedPath = Path.Combine(directory, "expected.ply");
            foreach (var withFaces in new[] { true, false })
            {
                plyWriter.Write(actualPath, paged.Vertices, withFaces ? paged.Faces : null);
                plyWriter.Write(expectedPath, memory.Vertices, withFaces ? memory.Faces : null);
                var bytes = File.ReadAllBytes(actualPath);
                Check(bytes.SequenceEqual(File.ReadAllBytes(expectedPath)), "Paged PLY did not include dirty records or changed encoding");
                var header = "ply\nformat binary_little_endian 1.0\nelement vertex 6007\nproperty float x\nproperty float y\nproperty float z\n";
                if (withFaces)
                {
                    header += "element face 6007\nproperty list uchar int vertex_indices\n";
                }
                header += "end_header\n";
                Check(Encoding.ASCII.GetString(bytes, 0, header.Length) == header, "PLY schema changed");
                var position = header.Length;
                foreach (var point in memory.Vertices)
                {
                    foreach (var coordinate in point)
                    {
                        var actual = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
                        Check(actual == BitConverter.SingleToInt32Bits((float)coordinate), "PLY coordinate precision or signed zero changed");
                        position += 4;
                    }
                }
                if (withFaces)
                {
                    foreach (var face in memory.Faces)
                    {
                        Check(bytes[position++] == 3, "PLY triangle lost its list count");
                        foreach (var vertex in face)
                        {
                            Check(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4)) == vertex, "PLY changed face winding or indices");
                            position += 4;
                        }
                    }
                }
                Check(position == bytes.Length, "PLY contains padding or trailing records");
            }
            plyWriter.Write(actualPath, Array.Empty<double[]>(), Array.Empty<int[]>());
            Check(Encoding.ASCII.GetString(File.ReadAllBytes(actualPath)).EndsWith("end_header\n", StringComparison.Ordinal), "Empty PLY contains a payload");
        }));
        tests.Run("Live cloud previews remain bounded without reading paged points", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var cloud = new DenseCloudData
            {
                ImageIds = ["a"],
                Samples = session.Open(Path.Combine(directory, "points.bin"), new DenseRecordCodec(), true)
            };
            foreach (var capacity in new[] { 2, 511, 512 })
            {
                cloud.Samples.Clear();
                var sampler = new CloudPreviewSampler(capacity);
                for (var index = 0; index < 50000; index++)
                {
                    var sample = new DenseSample(index, -index, 10, 3, 5, 7, 0, .75f);
                    cloud.Samples.Add(sample);
                    sampler.Add(sample);
                    if (index % 997 != 0 && index != 49999)
                    {
                        continue;
                    }
                    var before = session.Statistics;
                    var report = sampler.Report(cloud);
                    Check(session.Statistics == before, "Preview touched the disk-backed cloud");
                    Check(report.RetainedSamples == index + 1 && report.Preview.Count <= capacity, "Preview count or bound changed");
                    Check(report.Preview[0].Position[0] == 0, "Preview lost its first sample");
                    if (report.Preview.Count > 1)
                    {
                        var stride = report.Preview[1].Position[0];
                        for (var sampleIndex = 0; sampleIndex < report.Preview.Count; sampleIndex++)
                        {
                            Check(report.Preview[sampleIndex].Position[0] == sampleIndex * stride, "Preview is no longer uniformly sampled");
                        }
                        Check(report.Preview[^1].Position[0] >= index / 3.0, "Preview no longer spans the growing cloud");
                    }
                }
            }
        }));
        tests.Run("Paged cloud publication preserves every PLY byte including dirty records", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 0);
            var paged = session.Open(Path.Combine(directory, "points.bin"), new DenseRecordCodec(), true);
            var memory = new List<DenseSample>();
            for (var index = 0; index < 12000; index++)
            {
                var sample = new DenseSample(index, -0f, float.Epsilon, 17, 23, 251, index % 7, .12345f);
                paged.Add(sample);
                memory.Add(sample);
            }
            paged[3] = memory[3] = new DenseSample(-19, 0, 5, 255, 0, 1, 3, -0f);
            var expected = Path.Combine(directory, "memory.ply");
            var actual = Path.Combine(directory, "paged.ply");
            using var written = writes.BeginScope();
            clouds.Write(expected, new DenseCloudData { Samples = memory });
            clouds.Write(actual, new DenseCloudData { Samples = paged });
            Check(File.ReadAllBytes(expected).SequenceEqual(File.ReadAllBytes(actual)), "Encoded publication changed the PLY");
            foreach (var path in new[] { expected, actual })
            {
                Check(written.TryGetChecksum(path, out var checksum), "Publication must supply its checksum without rereading the cloud");
                using var file = File.OpenRead(path);
                Check(checksum == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file)), "Published checksum must match the full PLY");
            }
            using (var file = new FileStream(actual, FileMode.Open, FileAccess.Write))
            {
                file.Position = file.Length;
                file.WriteByte(0);
            }
            Check(!written.TryGetChecksum(actual, out _), "Modified staging files cannot reuse their publication checksum");
        }));
        tests.Run("Paged file handles are reused and bounded across concurrent reads", () => InDirectory(directory =>
        {
            using var cache = new PageCache(65536, 0);
            var files = new List<PageFile>();
            try
            {
                var first = new PageFile(Path.Combine(directory, "first.bin"), cache, true);
                files.Add(first);
                for (var page = 0; page < 40; page++)
                {
                    first.WriteBytes(page * 65536L, [(byte)page]);
                }
                Check(cache.HandleOpenCount == 1, "Sequential paging reopened the file per page");
                for (var index = 1; index <= 96; index++)
                {
                    var file = new PageFile(Path.Combine(directory, $"{index}.bin"), cache, true);
                    files.Add(file);
                    file.WriteBytes(0, [(byte)index]);
                    Check(cache.OpenHandleCount <= 32, "Handle pool exceeded its limit");
                }
                Parallel.For(1, files.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
                {
                    Span<byte> value = stackalloc byte[1];
                    files[index].ReadBytes(0, value);
                    Check(value[0] == index, "Handle eviction lost dirty data");
                });
                Span<byte> previous = stackalloc byte[1];
                for (var page = 0; page < 40; page++)
                {
                    first.ReadBytes(page * 65536L, previous);
                    Check(previous[0] == page, "Reopened handle read incorrect bytes");
                }
            }
            finally
            {
                foreach (var file in files)
                {
                    file.Dispose();
                }
            }
            Check(cache.OpenHandleCount == 0, "Disposed files retained handles");
        }));
        tests.Run("Fusion planning streams preserve grid coverage and report every preparation phase", () => InDirectory(directory =>
        {
            var fixture = new FusionFixture();
            var points = fixture.Cloud.Samples.ToArray();
            for (var repeat = 0; repeat < 5; repeat++)
            {
                foreach (var point in points)
                {
                    fixture.Cloud.Samples.Add(point);
                }
            }
            var resources = new FusionResourcePlan(25600000, 100000);
            var expected = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default);
            using var session = artifacts.OpenSession(65536, 0);
            var work = new WorkBuffers(session, directory);
            var updates = new List<OperationProgress>();
            var actual = FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, default,
                work: work, operationProgress: updates.Add);
            Check(expected.Keys.SequenceEqual(actual.Keys), "Streaming changed ordered coverage");
            Check(expected.VoxelSize == actual.VoxelSize, "Paging altered resolution");
            Check(updates.Select(update => update.Operation).Distinct().Count() == 5, "Missing planning phase");
            foreach (var phase in updates.GroupBy(update => update.Operation))
            {
                Check(phase.First().Completed == 0 && phase.Last().Completed == phase.Last().Total, "Phase lacks start or completion");
                Check(phase.Select(update => update.Completed).SequenceEqual(phase.Select(update => update.Completed).Order()), "Phase progress went backwards");
            }
            using var cancelled = new CancellationTokenSource();
            try
            {
                FusionGridBuilder.Build(fixture.Views, fixture.Cloud, fixture.Project.Settings, resources, cancelled.Token,
                    operationProgress: _ => cancelled.Cancel());
                throw new InvalidOperationException("Planning ignored cancellation");
            }
            catch (OperationCanceledException)
            {
            }
        }));
    }

    private void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-io-test-" + Guid.NewGuid().ToString("N"));
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
