using System.Text;

namespace Echo.Compute.Metal;

public sealed class MetalComputeDevice : IComputeDevice, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly MetalComputeLibrary library;
    private MetalComputeHandle? handle;
    private string device = "";
    private bool unavailable;

    public MetalComputeDevice()
    {
        var assembly = typeof(MetalComputeDevice).Assembly;
        var sources = assembly.GetManifestResourceNames().Where(name => name.EndsWith(".metal", StringComparison.Ordinal))
            .ToDictionary(name => name[(name.LastIndexOf("Native.", StringComparison.Ordinal) + 7)..], name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }, StringComparer.Ordinal);
        library = new MetalComputeLibrary(sources);
    }

    public string GetImplementation(ComputeOperation operation) => library.Implementation(operation);

    public IComputeSession? Open(CancellationToken cancellation, long memoryBudgetBytes)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }
        gate.Wait(cancellation);
        var transferred = false;
        try
        {
            if (unavailable)
            {
                return null;
            }
            if (handle == null)
            {
                var details = new StringBuilder(4096);
                IntPtr pointer;
                try
                {
                    pointer = MetalComputeNative.Create(library.Source, details, details.Capacity);
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    unavailable = true;
                    return null;
                }
                if (pointer == IntPtr.Zero)
                {
                    if (!details.ToString().Contains("No Metal compute device", StringComparison.Ordinal))
                    {
                        throw new ComputeException(details.ToString());
                    }
                    unavailable = true;
                    return null;
                }
                handle = new MetalComputeHandle(pointer);
                device = details.ToString();
            }
            var session = new MetalComputeSession(MetalComputeNative.Open(handle, memoryBudgetBytes), device, gate, cancellation);
            transferred = true;
            return session;
        }
        finally
        {
            if (!transferred)
            {
                gate.Release();
            }
        }
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            unavailable = true;
            handle?.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }
}
