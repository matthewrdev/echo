using System.Runtime.CompilerServices;
using Echo.Models;
using Echo.Telemetry;

namespace Echo.Compute;

internal sealed class ObservedComputeSession : IComputeStreamingSession
{
    private readonly IComputeSession session;
    private readonly Action<ComputeKernel> record;
    private readonly IPerformanceTrace trace;
    private double recordedGpu;

    public ObservedComputeSession(IComputeSession session, Action<ComputeKernel> record, IPerformanceTrace trace)
    {
        this.session = session;
        this.record = record;
        this.trace = trace;
    }

    public string Device => session.Device;

    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged
    {
        using var span = trace.Begin("Upload", PerformanceSpanKind.Transfer, PerformanceBackend.Metal);
        try
        {
            session.Upload(slot, values);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, (long)values.Length * Unsafe.SizeOf<T>());
        }
    }

    public bool UploadDescriptor(int slot, ReadOnlyMemory<float> values)
    {
        using var span = trace.Begin("Bind descriptor input", PerformanceSpanKind.Transfer, PerformanceBackend.Metal);
        var reused = false;
        try
        {
            reused = session.UploadDescriptor(slot, values);
            return reused;
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, values.Length, reused ? 0 : (long)values.Length * sizeof(float));
        }
    }

    public void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged
    {
        using var span = trace.Begin("Upload range", PerformanceSpanKind.Transfer, PerformanceBackend.Metal);
        try
        {
            session.UploadRange(slot, elementOffset, values);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, (long)values.Length * Unsafe.SizeOf<T>());
        }
    }

    public void Allocate(int slot, int byteCount)
    {
        using var span = trace.Begin("Allocate buffer", PerformanceSpanKind.Transfer, PerformanceBackend.Metal);
        try
        {
            session.Allocate(slot, byteCount);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, byteCount);
        }
    }

    public void Read<T>(int slot, Span<T> destination) where T : unmanaged
    {
        using var span = trace.Begin("Read results / wait", PerformanceSpanKind.Transfer, PerformanceBackend.Metal);
        try
        {
            session.Read(slot, destination);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, (long)destination.Length * Unsafe.SizeOf<T>());
        }
    }

    public void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters)
    {
        using var span = trace.Begin(kernel.ToString(), PerformanceSpanKind.Compute, PerformanceBackend.Metal);
        try
        {
            session.Dispatch(kernel, count, parameters);
            record(kernel);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, count, 0);
        }
    }

    public bool SupportsStreaming => session is IComputeStreamingSession { SupportsStreaming: true };

    public void DispatchBound(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters, ReadOnlySpan<int> slots)
    {
        using var span = trace.Begin(kernel.ToString(), PerformanceSpanKind.Compute, PerformanceBackend.Metal);
        try
        {
            ((IComputeStreamingSession)session).DispatchBound(kernel, count, parameters, slots);
            record(kernel);
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, count, 0);
        }
    }

    public void Submit()
    {
        using var span = trace.Begin("Submit Metal work", PerformanceSpanKind.Wait, PerformanceBackend.Metal);
        try
        {
            ((IComputeStreamingSession)session).Submit();
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, 0);
        }
    }

    public void Dispose()
    {
        using var span = trace.Begin("Drain Metal session", PerformanceSpanKind.Wait, PerformanceBackend.Metal);
        try
        {
            session.Dispose();
        }
        catch (OperationCanceledException)
        {
            span?.Complete(PerformanceStatus.Cancelled);
            throw;
        }
        catch
        {
            span?.Complete(PerformanceStatus.Failed);
            throw;
        }
        finally
        {
            Measure(span, null, 0);
        }
    }

    private void Measure(PerformanceTraceScope? span, long? items, long bytes)
    {
        var total = (session as IGpuTimingSource)?.CompletedGpuMilliseconds;
        var elapsed = total.HasValue ? Math.Max(0, total.Value - recordedGpu) : (double?)null;
        recordedGpu = total ?? 0;
        span?.Measure(items, bytes, elapsed);
    }
}
