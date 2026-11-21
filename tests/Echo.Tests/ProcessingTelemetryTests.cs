using System.Diagnostics;
using System.Text.Json;
using Echo.Contracts;
using Echo.Storage;
using Echo.Telemetry;

namespace Echo.Tests;

public sealed class ProcessingTelemetryTests
{
    private readonly IProcessTelemetryReader reader;

    public ProcessingTelemetryTests(IProcessTelemetryReader reader)
    {
        this.reader = reader;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Telemetry samples real processor and macOS thread activity", () =>
        {
            var tracker = new ProcessingTelemetryTracker(reader);
            int[] processes = [Environment.ProcessId];
            var first = tracker.Sample(processes);
            Check(first.CpuPercent == null, "The first sample has no interval baseline");
            Check(ReferenceEquals(first, tracker.Sample(processes)), "Frequent progress events should reuse the sample");
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 1.1)
            {
                Thread.SpinWait(10000);
            }

            var measured = tracker.Sample(processes);
            Check(measured.CpuPercent > 0, "CPU work must produce a positive measurement");
            Check(measured.LogicalProcessors == Environment.ProcessorCount, "CPU capacity must match the processor count");
            if (OperatingSystem.IsMacOS())
            {
                var threads = measured.Threads ?? throw new InvalidOperationException("Thread sampling unavailable");
                Check(threads.Count > 0, "macOS must report real thread samples");
                Check(threads.Any(thread => thread.CpuPercent > 0), "The busy thread must report CPU activity");
                Check(threads.All(thread => thread.ProcessId == Environment.ProcessId), "Do not include unrelated processes");
                Check(threads.All(thread => thread.ThreadId != 0), "Threads must have real identifiers");
                Check(threads.Select(thread => thread.ThreadId).Distinct().Count() == threads.Count,
                    "Thread identifiers must be unique within a process");
                Check(threads.All(thread => thread.CpuPercent is >= 0 and <= 100), "Thread percentages must use one-core capacity");
            }
        });
        tests.Run("Exited workers cannot interrupt telemetry", () =>
        {
            Check(reader.Read(int.MaxValue) == null, "An absent worker must be unavailable, not a sampling failure");
            var tracker = new ProcessingTelemetryTracker(reader);
            var sample = tracker.Sample([int.MaxValue]);
            Check(sample.CpuPercent == null && sample.Threads == null, "Missing samples must not imply idle processors");
        });
        tests.Run("Resource telemetry remains compatible with saved progress", () =>
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var old = JsonSerializer.Deserialize<ResourceProgress>("{\"workingSetBytes\":1024}", options)!;
            Check(old.Processor == null && old.WorkingSetBytes == 1024, "Old resources must remain readable");
            var resource = new ResourceProgress
            {
                Processor = new ProcessingTelemetryTracker(reader).Sample([Environment.ProcessId])
            };
            var restored = JsonSerializer.Deserialize<ResourceProgress>(JsonSerializer.Serialize(resource, options), options)!;
            Check(restored.Processor?.LogicalProcessors == Environment.ProcessorCount, "Processor capacity must round trip");
        });
        tests.Run("Run telemetry uses only explicitly registered workers", () =>
        {
            using var memory = new ProcessingMemoryTracker();
            Check(memory.ProcessIds().SequenceEqual([Environment.ProcessId]), "Default sampling must contain only the processor");
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
