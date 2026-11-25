using Echo.Geometry;
using Echo.Models;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Double.Solvers;
using MathNet.Numerics.LinearAlgebra.Solvers;

namespace Echo.Reconstruction;

internal static class CameraOverlapRefiner
{
    public static List<CameraOverlap> Gather(IReadOnlyList<CameraPose> cameras, IReadOnlyDictionary<string, Photo> photos,
        IReadOnlyList<ImagePair> pairs, CancellationToken cancellation)
    {
        var indices = Enumerable.Range(0, cameras.Count).ToDictionary(index => cameras[index].ImageId, StringComparer.Ordinal);
        var overlaps = new List<CameraOverlap>();
        foreach (var pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            var hasSupport = pair.Matches.Count >= 60
                && indices.TryGetValue(pair.A, out _) && indices.TryGetValue(pair.B, out _);
            if (!hasSupport)
            {
                continue;
            }

            var left = indices[pair.A];
            var right = indices[pair.B];
            overlaps.Add(new CameraOverlap(left, right, photos[pair.A], photos[pair.B], pair.Matches));
        }

        return overlaps;
    }

    public static double[] Errors(IReadOnlyList<CameraOverlap> overlaps, IReadOnlyList<CameraPose> cameras)
    {
        var frames = cameras.Select(pose => new CameraOverlapFrame(pose)).ToArray();
        return overlaps.Select(overlap => overlap.MedianError(frames)).ToArray();
    }

    public static bool HasConflictingOverlaps(double[] errors)
    {
        var broken = errors.Count(IsBroken);
        return broken >= 2 && errors.Count(IsGood) >= errors.Length / 2;
    }

    public static bool ImprovesOverlaps(double[] previous, double[] proposed)
    {
        var repaired = 0;
        var broken = 0;
        var stable = 0;
        var degraded = 0;
        for (var index = 0; index < previous.Length; index++)
        {
            if (IsBroken(previous[index]))
            {
                broken++;
                if (IsGood(proposed[index]))
                {
                    repaired++;
                }
            }

            if (IsGood(previous[index]))
            {
                stable++;
                if (proposed[index] > 4)
                {
                    degraded++;
                }
            }
        }

        var repairsConflicts = repaired * 2 >= broken && proposed.Count(IsBroken) * 2 <= broken;
        return repairsConflicts && degraded * 20 <= stable;
    }

    private static bool IsBroken(double error) => error > 12;

    private static bool IsGood(double error) => error <= 2;

    public static List<CameraPose> Refine(IReadOnlyList<CameraPose> cameras, IReadOnlyList<CameraOverlap> overlaps,
        V3[] positions, double[] weights, CancellationToken cancellation, Action<int>? progress = null)
    {
        var frames = cameras.Select(pose => new CameraOverlapFrame(pose)).ToArray();
        for (var index = 0; index < frames.Length; index++)
        {
            if (weights[index] > 0)
            {
                frames[index] = frames[index] with
                {
                    Centre = (frames[index].Centre + positions[index]) * .5
                };
            }
        }

        const int iterations = 24;
        var damping = .01;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(iteration);
            // Start with a wide loss so incompatible overlaps can move the cameras,
            // then tighten it as their geometry comes into agreement.
            var scale = Math.Max(1, 20 * Math.Pow(.83, iteration));
            var update = Solve(frames, overlaps, positions, weights, scale, damping, cancellation);
            if (update == null)
            {
                damping = Math.Min(1e5, damping * 4);
                continue;
            }

            var candidate = new CameraOverlapFrame[frames.Length];
            for (var index = 0; index < frames.Length; index++)
            {
                var offset = index * CameraOverlapFrame.ParameterCount;
                candidate[index] = frames[index].Rotate(new V3(update[offset + 3], update[offset + 4], update[offset + 5])) with
                {
                    Centre = frames[index].Centre + new V3(update[offset], update[offset + 1], update[offset + 2])
                };
            }

            if (Cost(candidate, overlaps, positions, weights, scale) < Cost(frames, overlaps, positions, weights, scale))
            {
                frames = candidate;
                damping = Math.Max(1e-5, damping * .6);
            }
            else
            {
                damping = Math.Min(1e5, damping * 4);
            }
        }

        return Enumerable.Range(0, frames.Length).Select(index => frames[index].ToPose(cameras[index])).ToList();
    }

    private static DenseVector? Solve(CameraOverlapFrame[] frames, IReadOnlyList<CameraOverlap> overlaps,
        V3[] positions, double[] weights, double scale, double damping, CancellationToken cancellation)
    {
        var size = CameraOverlapFrame.ParameterCount * frames.Length;
        var matrix = SparseMatrix.Create(size, size, 0);
        var rhs = new DenseVector(size);
        const double perturbation = 1e-5;
        var offsets = new[] { new V3(perturbation, 0, 0), new V3(0, perturbation, 0), new V3(0, 0, perturbation) };
        var rotated = new CameraOverlapFrame[frames.Length * 3];
        for (var index = 0; index < frames.Length; index++)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                rotated[index * 3 + axis] = frames[index].Rotate(offsets[axis]);
            }
        }

        foreach (var overlap in overlaps)
        {
            cancellation.ThrowIfCancellationRequested();
            AddOverlap(overlap, frames, rotated, offsets, perturbation, scale, matrix, rhs);
        }

        for (var index = 0; index < frames.Length; index++)
        {
            var offset = index * CameraOverlapFrame.ParameterCount;
            var difference = (frames[index].Centre - positions[index]) * weights[index];
            rhs[offset] -= difference.X;
            rhs[offset + 1] -= difference.Y;
            rhs[offset + 2] -= difference.Z;
            for (var axis = 0; axis < CameraOverlapFrame.ParameterCount; axis++)
            {
                var row = offset + axis;
                var prior = axis < 3 ? weights[index] : 0;
                matrix[row, row] = (matrix[row, row] + prior) * (1 + damping) + 1e-6;
            }
        }

        var update = new DenseVector(size);
        var iterator = new Iterator<double>(new IterationCountStopCriterion<double>(250),
            new ResidualStopCriterion<double>(1e-7), new FailureStopCriterion<double>());
        try
        {
            new BiCgStab().Solve(matrix, rhs, update, iterator, new DiagonalPreconditioner());
        }
        catch (NumericalBreakdownException)
        {
            return null;
        }

        cancellation.ThrowIfCancellationRequested();
        return update.All(double.IsFinite) ? update : null;
    }

    private static void AddOverlap(CameraOverlap overlap, CameraOverlapFrame[] frames, CameraOverlapFrame[] rotated,
        V3[] offsets, double perturbation, double scale, SparseMatrix matrix, DenseVector rhs)
    {
        const int parameters = CameraOverlapFrame.ParameterCount;
        const int pairParameters = parameters * 2;
        var left = frames[overlap.Left];
        var right = frames[overlap.Right];
        Span<int> indices = stackalloc int[pairParameters];
        Span<double> hessian = stackalloc double[pairParameters * pairParameters];
        Span<double> gradient = stackalloc double[pairParameters];
        Span<double> jacobian = stackalloc double[pairParameters];
        hessian.Clear();
        gradient.Clear();
        for (var axis = 0; axis < parameters; axis++)
        {
            indices[axis] = overlap.Left * parameters + axis;
            indices[axis + parameters] = overlap.Right * parameters + axis;
        }

        for (var sample = 0; sample < CameraOverlap.SampleCount; sample++)
        {
            var error = overlap.Error(left, right, sample);
            var weight = 1 / Math.Sqrt(1 + CameraGeometry.Square(error / scale));
            for (var axis = 0; axis < 3; axis++)
            {
                var moved = right with
                {
                    Centre = right.Centre + offsets[axis]
                };
                var translation = (overlap.Error(left, moved, sample) - error) / perturbation;
                jacobian[axis] = -translation;
                jacobian[axis + parameters] = translation;
                jacobian[axis + 3] = (overlap.Error(rotated[overlap.Left * 3 + axis], right, sample) - error) / perturbation;
                jacobian[axis + parameters + 3] = (overlap.Error(left, rotated[overlap.Right * 3 + axis], sample) - error) / perturbation;
            }

            for (var row = 0; row < pairParameters; row++)
            {
                var weighted = jacobian[row] * weight;
                gradient[row] -= weighted * error;
                for (var column = 0; column <= row; column++)
                {
                    hessian[row * pairParameters + column] += weighted * jacobian[column];
                }
            }
        }

        for (var row = 0; row < pairParameters; row++)
        {
            rhs[indices[row]] += gradient[row];
            for (var column = 0; column <= row; column++)
            {
                var value = hessian[row * pairParameters + column];
                matrix[indices[row], indices[column]] += value;
                if (column != row)
                {
                    matrix[indices[column], indices[row]] += value;
                }
            }
        }
    }

    private static double Cost(CameraOverlapFrame[] frames, IReadOnlyList<CameraOverlap> overlaps,
        V3[] positions, double[] weights, double scale)
    {
        double cost = 0;
        foreach (var overlap in overlaps)
        {
            for (var sample = 0; sample < CameraOverlap.SampleCount; sample++)
            {
                var error = overlap.Error(frames[overlap.Left], frames[overlap.Right], sample);
                cost += 2 * scale * scale * (Math.Sqrt(1 + CameraGeometry.Square(error / scale)) - 1);
            }
        }

        for (var index = 0; index < frames.Length; index++)
        {
            var difference = frames[index].Centre - positions[index];
            cost += weights[index] * difference.Dot(difference);
        }

        return cost;
    }
}
