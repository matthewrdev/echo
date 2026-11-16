using Echo.Contracts;
using System.Runtime.InteropServices;
using Echo.Compute;

namespace Echo.Texturing;

// Sparse symmetric system with explicit Dirichlet constraints. Used for chart
// least squares and positive-weight harmonic maps; no dense per-chart matrices.
internal sealed class TextureLinearSystem
{
    private readonly Dictionary<int, double>[] rows;

    public TextureLinearSystem(int size)
    {
        rows = Enumerable.Range(0, size).Select(_ => new Dictionary<int, double>()).ToArray();
    }

    public void Add(int row, int column, double value)
    {
        ref var coefficient = ref CollectionsMarshal.GetValueRefOrAddDefault(rows[row], column, out _);
        coefficient += value;
    }

    public double[]? Solve(IReadOnlyDictionary<int, double> constraints, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        var count = rows.Length;
        var offsets = new int[count + 1];
        var columns = new List<int>();
        var coefficients = new List<double>();
        var diagonal = new double[count];
        var fixedRows = new bool[count];
        var solution = new double[count];
        foreach (var entry in constraints)
        {
            fixedRows[entry.Key] = true;
            solution[entry.Key] = entry.Value;
        }
        for (var row = 0; row < count; row++)
        {
            foreach (var entry in rows[row])
            {
                columns.Add(entry.Key);
                coefficients.Add(entry.Value);
            }
            offsets[row + 1] = columns.Count;
            diagonal[row] = rows[row].GetValueOrDefault(row);
            if (!fixedRows[row] && (!(diagonal[row] > 0) || !double.IsFinite(diagonal[row])))
            {
                return null;
            }
        }
        var indices = columns.ToArray();
        var values = coefficients.ToArray();
        var residual = new double[count];
        var direction = new double[count];
        var product = new double[count];
        Multiply(offsets, indices, values, solution, product, fixedRows);
        var energy = 0.0;
        for (var row = 0; row < count; row++)
        {
            if (fixedRows[row])
            {
                continue;
            }
            residual[row] = -product[row];
            direction[row] = residual[row] / diagonal[row];
            energy += residual[row] * direction[row];
        }
        if (!double.IsFinite(energy))
        {
            return null;
        }
        var tolerance = Math.Max(1e-28, energy * 1e-20);
        var warm = TextureSolveCompute.TryWarmStart(compute, backend, offsets, indices, values, diagonal, fixedRows, solution, cancellation);
        if (warm != null)
        {
            Multiply(offsets, indices, values, warm, product, fixedRows);
            var warmEnergy = 0.0;
            for (var row = 0; row < count; row++)
            {
                if (!fixedRows[row])
                {
                    warmEnergy += product[row] * product[row] / diagonal[row];
                }
            }
            if (double.IsFinite(warmEnergy) && warmEnergy < energy * .5)
            {
                solution = warm;
                energy = warmEnergy;
                for (var row = 0; row < count; row++)
                {
                    if (!fixedRows[row])
                    {
                        residual[row] = -product[row];
                        direction[row] = residual[row] / diagonal[row];
                    }
                }
            }
        }
        for (var iteration = 0; iteration < Math.Min(4000, count * 4 + 32); iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (energy <= tolerance)
            {
                return solution;
            }
            Multiply(offsets, indices, values, direction, product, fixedRows);
            var denominator = 0.0;
            for (var row = 0; row < count; row++)
            {
                denominator += direction[row] * product[row];
            }
            if (!(denominator > 0) || !double.IsFinite(denominator))
            {
                return null;
            }
            var step = energy / denominator;
            var nextEnergy = 0.0;
            for (var row = 0; row < count; row++)
            {
                if (fixedRows[row])
                {
                    continue;
                }
                solution[row] += step * direction[row];
                residual[row] -= step * product[row];
                nextEnergy += residual[row] * residual[row] / diagonal[row];
            }
            var beta = nextEnergy / energy;
            for (var row = 0; row < count; row++)
            {
                if (!fixedRows[row])
                {
                    direction[row] = residual[row] / diagonal[row] + beta * direction[row];
                }
            }
            energy = nextEnergy;
        }
        return null;
    }

    private void Multiply(int[] offsets, int[] indices, double[] values, double[] vector,
        double[] result, bool[] fixedRows)
    {
        for (var row = 0; row < result.Length; row++)
        {
            var sum = 0.0;
            if (!fixedRows[row])
            {
                for (var entry = offsets[row]; entry < offsets[row + 1]; entry++)
                {
                    sum += values[entry] * vector[indices[entry]];
                }
            }
            result[row] = sum;
        }
    }
}
