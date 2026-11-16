using System.Diagnostics;

namespace Echo.Storage;

// Owned by one run. Workers register explicitly so unrelated Echo processes
// are never counted or stopped by another run's resource policy.
public sealed class ProcessingMemoryTracker : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<int, Process> workers = [];
    private readonly Process process;
    private long peak;

    public ProcessingMemoryTracker()
    {
        process = Process.GetCurrentProcess();
    }

    public IDisposable RegisterWorker(int id)
    {
        lock (gate)
        {
            workers.Add(id, Process.GetProcessById(id));
        }
        return new WorkerMemoryRegistration(this, id);
    }

    public IReadOnlyList<int> ProcessIds()
    {
        lock (gate)
        {
            return new[] { process.Id }.Concat(workers.Keys).ToArray();
        }
    }

    public MemoryUsage Sample()
    {
        lock (gate)
        {
            process.Refresh();
            var current = process.WorkingSet64;
            foreach (var worker in workers.Values)
            {
                try
                {
                    worker.Refresh();
                    if (!worker.HasExited)
                    {
                        current += worker.WorkingSet64;
                    }
                }
                catch (InvalidOperationException)
                {
                    // A worker can finish between Refresh and the memory query.
                }
            }
            peak = Math.Max(peak, current);
            return new MemoryUsage(current, peak, GC.GetTotalMemory(false));
        }
    }

    internal void Remove(int id)
    {
        lock (gate)
        {
            if (workers.Remove(id, out var worker))
            {
                worker.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var worker in workers.Values)
            {
                worker.Dispose();
            }
            workers.Clear();
            process.Dispose();
        }
    }
}
