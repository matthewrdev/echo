using System.Diagnostics;
using System.Text;
using Echo.DenseCloud;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Artifacts;
using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Benchmarks;

public sealed class CloudPublicationBenchmark
{
    private readonly ICloudBuilder clouds;
    private readonly IBulkArtifactStore bulk;
    private readonly IArtifactWriteObserver writes;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IProjectStoreFactory stores;

    public CloudPublicationBenchmark(ICloudBuilder clouds, IBulkArtifactStore bulk, IArtifactWriteObserver writes,
        IFileOperations files, IJsonCodec json, IProjectStoreFactory stores)
    {
        this.clouds = clouds;
        this.bulk = bulk;
        this.writes = writes;
        this.files = files;
        this.json = json;
        this.stores = stores;
    }

    public int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks cloud-publication PROJECT RUN NEW_OUTPUT");
            return 2;
        }

        var output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new IOException("Choose a new output directory.");
        }

        var store = stores.Open(args[0]);
        var run = store.GetRun(args[1]);
        var folder = store.PathFor(run.Artifacts["cloud"]);
        var source = Path.Combine(folder, "cloud.ply");
        var expected = files.HashFile(source);
        var bytes = new FileInfo(source).Length;
        var report = new DenseCloudReportCodec().Read(Path.Combine(folder, "cloud.bin"));
        Directory.CreateDirectory(output);
        var measurements = new List<CloudPublicationMeasurement>();
        for (var trial = 0; trial < 3; trial++)
        {
            foreach (var streamed in trial % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                using var session = bulk.OpenSession(4 * 1024 * 1024, 0);
                var cloud = clouds.Read(source, report, session);
                var implementation = streamed ? "checksum-during-write" : "write-then-reread-checksum";
                var path = Path.Combine(output, implementation + ".ply");
                using var written = writes.BeginScope();
                var start = Stopwatch.GetTimestamp();
                string checksum;
                if (streamed)
                {
                    clouds.Write(path, cloud);
                    if (!written.TryGetChecksum(path, out checksum))
                    {
                        throw new InvalidDataException("Cloud publication did not supply its checksum.");
                    }
                }
                else
                {
                    WritePrevious(path, cloud);
                    checksum = files.HashFile(path);
                }

                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (checksum != expected)
                {
                    throw new InvalidDataException("Cloud PLY bytes changed.");
                }

                measurements.Add(new CloudPublicationMeasurement(implementation, trial, elapsed, streamed ? 0 : bytes, checksum));
            }
        }

        foreach (var path in Directory.EnumerateFiles(output, "*.ply"))
        {
            if (files.HashFile(path) != expected)
            {
                throw new InvalidDataException("Persisted cloud checksum differs from the publication result.");
            }
        }

        var result = new
        {
            source,
            bytes,
            report.RetainedSamples,
            note = "Three alternating trials with a warm filesystem cache. Both paths include durable flush and checksum. Checksum reread bytes are logical reads, not physical SSD traffic. Output hashes must match the saved cloud exactly.",
            measurements
        };
        files.AtomicJson(Path.Combine(output, "comparison.json"), result);
        Console.WriteLine(json.Encode(result));
        return 0;
    }

    private void WritePrevious(string path, DenseCloudData cloud)
    {
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes($"ply\nformat binary_little_endian 1.0\nelement vertex {cloud.Samples.Count}\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"));
        ((PagedList<DenseSample>)cloud.Samples).CopyEncodedRecordsTo(stream);
        stream.Flush(true);
    }
}
