using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage;

namespace Echo.Benchmarks;

public sealed class SceneStorageBenchmark
{
    private readonly IBulkArtifactStore bulk;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly JsonTypeInfo<ReconstructionScene> sceneType;
    private readonly JsonTypeInfo<ReconstructionOverview> overviewType;

    public SceneStorageBenchmark(IBulkArtifactStore bulk, IFileOperations files, IJsonCodec json,
        IEnumerable<IJsonReadMetadata> metadata)
    {
        this.bulk = bulk;
        this.files = files;
        this.json = json;
        sceneType = metadata.Select(source => source.GetTypeInfo(typeof(ReconstructionScene)))
            .OfType<JsonTypeInfo<ReconstructionScene>>().Single();
        overviewType = metadata.Select(source => source.GetTypeInfo(typeof(ReconstructionOverview)))
            .OfType<JsonTypeInfo<ReconstructionOverview>>().Single();
    }

    public int Run(string[] args)
    {
        if (args.Length != 2)
        {
            throw new EchoException("scene-storage requires a JSON scene fixture and a new output directory.", 2);
        }

        var source = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output))
        {
            throw new EchoException("Use a new output directory for the storage benchmark.", 2);
        }

        Directory.CreateDirectory(output);
        // JSON input exists only for this explicit storage comparison. Production
        // readers require the new binary scene contract.
        ReconstructionScene ReadJson()
        {
            using var stream = File.OpenRead(source);
            return JsonSerializer.Deserialize(stream, sceneType) ?? throw new InvalidDataException("Missing scene fixture.");
        }
        var scene = ReadJson();
        var records = new List<SceneIoMeasurement>();
        var binary = Path.Combine(output, "scene.json");
        files.AtomicData(binary, scene);
        Verify(scene, bulk.Read<ReconstructionScene>(binary));
        for (var iteration = 0; iteration < 3; iteration++)
        {
            Measure("JSON full read", iteration, () => Verify(scene, ReadJson()), records);
            Measure("Binary full read", iteration, () => Verify(scene, bulk.Read<ReconstructionScene>(binary)), records);
            Measure("JSON camera metadata", iteration, () =>
            {
                using var stream = File.OpenRead(source);
                JsonSerializer.Deserialize(stream, overviewType);
            }, records);
            Measure("Binary camera metadata", iteration, () => bulk.Read<ReconstructionOverview>(binary), records);
            Measure("Binary paged open", iteration, () =>
            {
                using var session = bulk.OpenSession(65536, 0);
                var paged = session.Read<ReconstructionScene>(binary);
                if (paged.Points.Count != scene.Points.Count)
                {
                    throw new InvalidDataException("Paged point count changed.");
                }
            }, records);
            var jsonOutput = Path.Combine(output, "write-json-" + iteration + ".json");
            Measure("JSON write + durable flush", iteration, () => files.AtomicJson(jsonOutput, scene), records);
            File.Delete(jsonOutput);
            var binaryOutput = Path.Combine(output, "write-binary-" + iteration);
            Measure("Binary write + durable flush", iteration,
                () => files.AtomicData(Path.Combine(binaryOutput, "scene.json"), scene), records);
            Directory.Delete(binaryOutput, true);
        }

        var bytes = Directory.GetFiles(output).Sum(file => new FileInfo(file).Length);
        var report = new
        {
            source,
            sourceBytes = new FileInfo(source).Length,
            binaryBytes = bytes,
            metadataBytes = new FileInfo(binary).Length,
            points = scene.Points.Count,
            cameras = scene.Cameras.Count,
            note = "Three warm-cache trials. Full reads include identical value verification. Writes include durable flush. Paging-open timing excludes visiting points. Concurrent machine work may affect timings.",
            measurements = records
        };
        files.AtomicJson(Path.Combine(output, "comparison.json"), report);
        Console.WriteLine(json.Encode(report));
        return 0;
    }

    private void Measure(string operation, int iteration, Action action, List<SceneIoMeasurement> records)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocations = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        action();
        records.Add(new SceneIoMeasurement
        {
            Operation = operation,
            Iteration = iteration,
            Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocations
        });
    }

    private void Verify(ReconstructionScene expected, ReconstructionScene actual)
    {
        if (expected.Points.Count != actual.Points.Count || expected.Cameras.Count != actual.Cameras.Count)
        {
            throw new InvalidDataException("Scene counts changed.");
        }

        for (var index = 0; index < expected.Points.Count; index++)
        {
            var a = expected.Points[index];
            var b = actual.Points[index];
            var sameVectors = Same(a.Position, b.Position) && Same(a.PixelA, b.PixelA) && Same(a.PixelB, b.PixelB);
            var sameIdentity = a.ImageA == b.ImageA && a.ImageB == b.ImageB;
            var sameValues = BitConverter.DoubleToInt64Bits(a.Error) == BitConverter.DoubleToInt64Bits(b.Error)
                && (a.Colour == null ? b.Colour == null : b.Colour != null && a.Colour.SequenceEqual(b.Colour));
            if (!sameVectors || !sameIdentity || !sameValues)
            {
                throw new InvalidDataException($"Sparse point {index} changed.");
            }
        }
    }

    private bool Same(double[] a, double[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var index = 0; index < a.Length; index++)
        {
            if (BitConverter.DoubleToInt64Bits(a[index]) != BitConverter.DoubleToInt64Bits(b[index]))
            {
                return false;
            }
        }

        return true;
    }
}
