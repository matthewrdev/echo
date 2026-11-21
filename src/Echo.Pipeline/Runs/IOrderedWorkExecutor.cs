namespace Echo.Pipeline.Runs;

public interface IOrderedWorkExecutor
{
    void Run<T>(int count, int workers, Func<int, T> execute, Action<int, T> publish, CancellationToken cancellation);
}
