using Echo.Models;
using System.Runtime.InteropServices;
using Echo.Compute;

namespace Echo.Texturing.Charts;

// Sparse symmetric system with explicit Dirichlet constraints. Used for chart
// least squares and positive-weight harmonic maps; no dense per-chart matrices.
internal sealed class TextureLinearSystem
{
    private Dictionary<int, double>[] rows = [];
    private int[] columnIndices = [];
    private double[] coefficientValues = [];
    private int[] rowOffsets = new int[1];
    private double[] diagonalValues = [];
    private bool[] fixedRowFlags = [];
    private double[] residualValues = [];
    private double[] directionValues = [];
    private double[] productValues = [];
    private int count;

    public TextureLinearSystem(int size)
    {
        Reset(size);
    }

    public void Reset(int size)
    {
        for (var row = 0; row < count; row++)
        {
            rows[row].Clear();
        }

        if (size > rows.Length)
        {
            var previous = rows.Length;
            Array.Resize(ref rows, size);
            for (var row = previous; row < size; row++)
            {
                rows[row] = new Dictionary<int, double>();
            }

            rowOffsets = new int[size + 1];
            diagonalValues = new double[size];
            fixedRowFlags = new bool[size];
            residualValues = new double[size];
            directionValues = new double[size];
            productValues = new double[size];
        }

        count = size;
    }

    public void Add(int row, int column, double value)
    {
        ref var coefficient = ref CollectionsMarshal.GetValueRefOrAddDefault(rows[row], column, out _);
        coefficient += value;
    }

    public double[]? Solve(IReadOnlyDictionary<int, double> constraints, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        var diagonal = diagonalValues;
        var fixedRows = fixedRowFlags;
        fixedRows.AsSpan(0, count).Clear();
        var solution = new double[count];
        foreach (var entry in constraints)
        {
            fixedRows[entry.Key] = true;
            solution[entry.Key] = entry.Value;
        }

        var entries = 0;
        for (var row = 0; row < count; row++)
        {
            var required = entries + rows[row].Count;
            if (required > columnIndices.Length)
            {
                var capacity = Math.Max(required, columnIndices.Length * 2);
                Array.Resize(ref columnIndices, capacity);
                Array.Resize(ref coefficientValues, capacity);
            }

            foreach (var entry in rows[row])
            {
                columnIndices[entries] = entry.Key;
                coefficientValues[entries++] = entry.Value;
            }

            diagonal[row] = rows[row].GetValueOrDefault(row);
            if (!fixedRows[row] && (!(diagonal[row] > 0) || !double.IsFinite(diagonal[row])))
            {
                return null;
            }

            rowOffsets[row + 1] = entries;
        }

        var residual = residualValues;
        var direction = directionValues;
        var product = productValues;
        residual.AsSpan(0, count).Clear();
        direction.AsSpan(0, count).Clear();
        Multiply(solution, product);
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
        var warm = TextureSolveCompute.TryWarmStart(compute, backend, rowOffsets.AsSpan(0, count + 1),
            columnIndices.AsSpan(0, entries), coefficientValues.AsSpan(0, entries), diagonal.AsSpan(0, count),
            fixedRows.AsSpan(0, count), solution, cancellation);
        if (warm != null)
        {
            Multiply(warm, product);
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

            Multiply(direction, product);
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

    private void Multiply(double[] vector, double[] result)
    {
        var offsets = rowOffsets;
        var indices = columnIndices;
        var values = coefficientValues;
        for (var row = 0; row < count; row++)
        {
            var sum = 0.0;
            if (!fixedRowFlags[row])
            {
                var end = offsets[row + 1];
                for (var entry = offsets[row]; entry < end; entry++)
                {
                    sum += values[entry] * vector[indices[entry]];
                }
            }

            result[row] = sum;
        }
    }
}
