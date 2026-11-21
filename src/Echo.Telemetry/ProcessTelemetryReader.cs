using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Contracts;

namespace Echo.Telemetry;

public sealed class ProcessTelemetryReader : IProcessTelemetryReader
{
    public ProcessTelemetryReader()
    {
    }

    public ProcessCounters? Read(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessCounters
            {
                ProcessId = processId,
                CpuTime = process.TotalProcessorTime,
                Threads = OperatingSystem.IsMacOS() ? ReadMacThreads(processId) : null
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Sampling must not interrupt reconstruction if a native worker exits.
            return null;
        }
    }

    private List<ThreadProgress>? ReadMacThreads(int processId)
    {
        // XNU's PROC_PIDLISTTHREADIDS (28) includes kernel-created threads with
        // unique IDs, read with PROC_PIDTHREADID64INFO (15). Older systems can
        // fall back to pthread identifiers (6/5). TH_USAGE_SCALE is 1000.
        var listFlavor = 28;
        var threadFlavor = 15;
        var identifiers = new ulong[256];
        var bytes = MacProcessNative.ListThreads(processId, listFlavor, 0, identifiers, identifiers.Length * sizeof(ulong));
        if (bytes <= 0)
        {
            listFlavor = 6;
            threadFlavor = 5;
            bytes = MacProcessNative.ListThreads(processId, listFlavor, 0, identifiers, identifiers.Length * sizeof(ulong));
        }
        while (bytes == identifiers.Length * sizeof(ulong) && identifiers.Length < 32768)
        {
            identifiers = new ulong[identifiers.Length * 2];
            bytes = MacProcessNative.ListThreads(processId, listFlavor, 0, identifiers, identifiers.Length * sizeof(ulong));
        }
        if (bytes <= 0)
        {
            return null;
        }

        var threads = new List<ThreadProgress>();
        var size = Marshal.SizeOf<MacThreadInfo>();
        foreach (var identifier in identifiers.Take(bytes / sizeof(ulong)).Where(value => value != 0).Distinct())
        {
            if (MacProcessNative.ReadThread(processId, threadFlavor, identifier, out var info, size) != size)
            {
                continue;
            }

            threads.Add(new ThreadProgress
            {
                ProcessId = processId,
                ThreadId = identifier,
                Name = info.name ?? "",
                CpuPercent = Math.Clamp(info.cpuUsage / 10.0, 0, 100)
            });
        }
        return threads;
    }
}
