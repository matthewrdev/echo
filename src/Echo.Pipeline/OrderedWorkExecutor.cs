using System.ComponentModel.Composition;
using System.Runtime.ExceptionServices;

namespace Echo.Pipeline;

[Export(typeof(IOrderedWorkExecutor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OrderedWorkExecutor : IOrderedWorkExecutor
{
    [ImportingConstructor]
    public OrderedWorkExecutor()
    {
    }

    public void Run<T>(int count, int workers, Func<int, T> execute, Action<int, T> publish, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        cancellation.ThrowIfCancellationRequested();
        if (count == 0)
        {
            return;
        }
        if (workers == 1)
        {
            for (var index = 0; index < count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var result = execute(index);
                cancellation.ThrowIfCancellationRequested();
                publish(index, result);
            }
            return;
        }

        var capacity = Math.Min(count, workers);
        var results = new T[capacity];
        var options = new ParallelOptions { MaxDegreeOfParallelism = capacity, CancellationToken = cancellation };
        for (var first = 0; first < count; first += capacity)
        {
            var length = Math.Min(capacity, count - first);
            try
            {
                Parallel.For(0, length, options, slot => results[slot] = execute(first + slot));
            }
            catch (AggregateException error)
            {
                // Parallel.For has joined every worker. Preserve cancellation and the
                // original exception type for a single failed unit (including EchoException).
                cancellation.ThrowIfCancellationRequested();
                ExceptionDispatchInfo.Capture(error.GetBaseException()).Throw();
                throw;
            }

            // Only the caller publishes indexes and previews. Bounded waves prevent
            // a slow first image from retaining an unbounded queue of later results.
            for (var slot = 0; slot < length; slot++)
            {
                cancellation.ThrowIfCancellationRequested();
                publish(first + slot, results[slot]);
                results[slot] = default!;
            }
        }
    }
}
