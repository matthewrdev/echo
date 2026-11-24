using System.Runtime.CompilerServices;
using System.Text;

namespace Echo.Compute.Metal;

public sealed class MetalComputeDevice : IComputeDevice, IDisposable
{
    private const int SessionSlots = 2;
    private readonly SemaphoreSlim slots = new(SessionSlots, SessionSlots);
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly object initialization = new();
    private readonly ConditionalWeakTable<float[], DescriptorBufferIdentity> descriptorIdentities = new();
    private int nextDescriptorIdentity;
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
                return MetalShaderSource.WithoutDocumentation(reader.ReadToEnd());
            }, StringComparer.Ordinal);
        library = new MetalComputeLibrary(sources);
    }

    public string GetImplementation(ComputeOperation operation) => library.Implementation(operation);

    public IComputeSession? Open(CancellationToken cancellation, long memoryBudgetBytes) =>
        OpenSession(cancellation, memoryBudgetBytes, SessionSlots);

    public IComputeSession? OpenForMatching(CancellationToken cancellation, long memoryBudgetBytes, long requiredBytes)
    {
        var dividedBudget = memoryBudgetBytes / SessionSlots;
        return requiredBytes <= dividedBudget
            ? OpenSession(cancellation, dividedBudget, 1)
            : OpenSession(cancellation, memoryBudgetBytes, SessionSlots);
    }

    private IComputeSession? OpenSession(CancellationToken cancellation, long memoryBudgetBytes, int permits)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        Reserve(permits, cancellation);
        var transferred = false;
        try
        {
            if (!EnsureDevice())
            {
                return null;
            }

            var session = new MetalComputeSession(MetalComputeNative.Open(handle!, memoryBudgetBytes), device,
                () => slots.Release(permits), DescriptorIdentity, cancellation);
            transferred = true;
            return session;
        }
        finally
        {
            if (!transferred)
            {
                slots.Release(permits);
            }
        }
    }

    private uint DescriptorIdentity(float[] array) =>
        descriptorIdentities.GetValue(array, _ => new DescriptorBufferIdentity(unchecked((uint)Interlocked.Increment(ref nextDescriptorIdentity)))).Value;

    private bool EnsureDevice()
    {
        lock (initialization)
        {
            if (unavailable)
            {
                return false;
            }

            if (handle != null)
            {
                return true;
            }

            var details = new StringBuilder(4096);
            IntPtr pointer;
            try
            {
                pointer = MetalComputeNative.Create(library.Source, details, details.Capacity);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                unavailable = true;
                return false;
            }

            if (pointer == IntPtr.Zero)
            {
                if (!details.ToString().Contains("No Metal compute device", StringComparison.Ordinal))
                {
                    throw new ComputeException(details.ToString());
                }

                unavailable = true;
                return false;
            }

            handle = new MetalComputeHandle(pointer);
            device = details.ToString();
            return true;
        }
    }

    private void Reserve(int permits, CancellationToken cancellation)
    {
        admission.Wait(cancellation);
        var acquired = 0;
        try
        {
            while (acquired < permits)
            {
                slots.Wait(cancellation);
                acquired++;
            }
        }
        catch
        {
            if (acquired > 0)
            {
                slots.Release(acquired);
            }

            throw;
        }
        finally
        {
            admission.Release();
        }
    }

    public void Dispose()
    {
        Reserve(SessionSlots, CancellationToken.None);
        try
        {
            unavailable = true;
            handle?.Dispose();
        }
        finally
        {
            slots.Release(SessionSlots);
        }
    }
}
