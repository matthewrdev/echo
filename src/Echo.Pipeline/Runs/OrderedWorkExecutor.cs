using System.Runtime.ExceptionServices;

namespace Echo.Pipeline.Runs;

public sealed class OrderedWorkExecutor : IOrderedWorkExecutor
{
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

        // A small look-ahead absorbs uneven jobs without retaining an unbounded
        // queue behind an early straggler. Only the caller publishes results.
        var capacity = (int)Math.Min(count, (long)workers * 2);
        var pending = new Task<T>?[capacity];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var permits = new SemaphoreSlim(workers, workers);
        ExceptionDispatchInfo? failure = null;

        Task<T> Start(int index) => Task.Run(async () =>
        {
            var entered = false;
            try
            {
                await permits.WaitAsync(stop.Token).ConfigureAwait(false);
                entered = true;
                stop.Token.ThrowIfCancellationRequested();
                return execute(index);
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(error), null);
                stop.Cancel();
                throw;
            }
            finally
            {
                if (entered)
                {
                    permits.Release();
                }
            }
        });

        try
        {
            for (var index = 0; index < capacity; index++)
            {
                pending[index] = Start(index);
            }

            for (var index = 0; index < count; index++)
            {
                var slot = index % capacity;
                var result = pending[slot]!.GetAwaiter().GetResult();
                stop.Token.ThrowIfCancellationRequested();
                publish(index, result);
                pending[slot] = null;
                if (index + capacity < count)
                {
                    pending[slot] = Start(index + capacity);
                }
            }
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(error), null);
        }
        finally
        {
            stop.Cancel();
            try
            {
                Task.WhenAll(pending.OfType<Task<T>>()).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // All workers have joined; report the original failure below.
            }
        }

        failure?.Throw();
        cancellation.ThrowIfCancellationRequested();
    }
}
