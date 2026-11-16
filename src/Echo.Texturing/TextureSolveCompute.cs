using Echo.Contracts;
using System.Buffers;
using Echo.Compute;

namespace Echo.Texturing;

internal static class TextureSolveCompute
{
    public static double[]? TryWarmStart(IComputeService? compute, ComputeBackend backend, int[] offsets, int[] indices,
        double[] coefficients, double[] diagonal, bool[] fixedRows, double[] initial, CancellationToken cancellation)
    {
        // Float warm starts did not beat the double solve in measured charts.
        // Keep them opt-in for explicit Metal comparisons, never enable in Auto.
        var eligible = backend == ComputeBackend.Metal && initial.Length >= 2048;
        if (compute == null || !eligible)
        {
            return null;
        }
        if (!compute.CanAllocate((long)coefficients.Length * 8 + (long)initial.Length * 32 + 4, backend))
        {
            return null;
        }
        using var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var values = ArrayPool<float>.Shared.Rent(coefficients.Length);
        var state = ArrayPool<float>.Shared.Rent(initial.Length * 4);
        var constraints = ArrayPool<float>.Shared.Rent(initial.Length * 2);
        try
        {
            for (var i = 0; i < coefficients.Length; i++)
            {
                values[i] = (float)coefficients[i];
            }
            state.AsSpan(0, initial.Length * 4).Clear();
            for (var i = 0; i < initial.Length; i++)
            {
                state[i * 4 + 3] = fixedRows[i] ? 1 : (float)diagonal[i];
                constraints[i * 2] = fixedRows[i] ? 1 : 0;
                constraints[i * 2 + 1] = (float)initial[i];
            }
            session.Upload<int>(0, offsets);
            session.Upload<int>(1, indices);
            session.Upload<float>(2, values.AsSpan(0, coefficients.Length));
            session.Upload<float>(3, state.AsSpan(0, initial.Length * 4));
            session.Upload<float>(4, constraints.AsSpan(0, initial.Length * 2));
            session.Allocate(5, initial.Length * sizeof(float));
            session.Dispatch("uv_warm_start", 256, [initial.Length]);
            session.Read<float>(3, state.AsSpan(0, initial.Length * 4));
            var result = new double[initial.Length];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = fixedRows[i] ? initial[i] : state[i * 4];
            }
            return result;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            return null;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(values);
            ArrayPool<float>.Shared.Return(state);
            ArrayPool<float>.Shared.Return(constraints);
        }
    }
}
