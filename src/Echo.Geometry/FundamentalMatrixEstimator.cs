using Echo.Models;
using Echo.Compute;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Geometry;

/// <summary>
/// Fits the fundamental matrix between two photos and rejects correspondences
/// that disagree with their shared epipolar geometry.
/// </summary>
public static class FundamentalMatrixEstimator
{
    public static FundamentalMatrixEstimate? Estimate(IReadOnlyList<Match> matches, double thresholdPixels, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        if (matches.Count < 12)
        {
            return null;
        }
        if (!double.IsFinite(thresholdPixels) || thresholdPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdPixels));
        }
        var random = new Random(817);
        var sample = new Match[8];
        var best = new List<Match>();
        var inliers = new List<Match>(matches.Count);
        double[]? bestMatrix = null;
        var maximumIterations = 8192;
        var iteration = 0;
        var squaredThreshold = thresholdPixels * thresholdPixels;
        using var acceleration = EpipolarCompute.Open(compute, backend, matches, cancellation);
        var batchSize = acceleration == null ? 1 : 32;
        var matrices = new double[]?[batchSize];
        var upperBounds = new int[batchSize];
        var failed = false;
        for (; iteration < maximumIterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            var slot = iteration % batchSize;
            if (slot == 0)
            {
                var count = Math.Min(batchSize, maximumIterations - iteration);
                for (var hypothesis = 0; hypothesis < count; hypothesis++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    RandomSampleSelector.Select<Match>(matches, random, sample);
                    matrices[hypothesis] = Fit(sample);
                }
                Array.Fill(upperBounds, int.MaxValue);
                if (acceleration != null && !failed)
                {
                    try
                    {
                        EpipolarCompute.Count(acceleration, matrices, count, matches.Count, thresholdPixels, upperBounds);
                    }
                    catch (ComputeException) when (backend == ComputeBackend.Auto)
                    {
                        failed = true;
                    }
                }
            }
            if (upperBounds[slot] <= best.Count)
            {
                continue;
            }
            var matrix = matrices[slot];
            if (matrix == null)
            {
                continue;
            }
            Collect(matches, matrix, squaredThreshold, inliers);
            if (inliers.Count < 12 || inliers.Count <= best.Count)
            {
                continue;
            }
            // Refit promising consensuses before scoring the next hypothesis. This is
            // especially useful when only a small part of two photographs overlaps.
            for (var refinement = 0; refinement < 3; refinement++)
            {
                var refined = Fit(inliers);
                if (refined == null)
                {
                    break;
                }
                var previousCount = inliers.Count;
                var refinedInliers = new List<Match>(matches.Count);
                Collect(matches, refined, squaredThreshold, refinedInliers);
                if (refinedInliers.Count < previousCount)
                {
                    break;
                }
                matrix = refined;
                inliers = refinedInliers;
                if (inliers.Count == previousCount)
                {
                    break;
                }
            }
            if (inliers.Count <= best.Count)
            {
                continue;
            }
            var previous = best;
            best = inliers;
            inliers = previous;
            bestMatrix = matrix;
            var successProbability = Math.Pow((double)best.Count / matches.Count, 8);
            var required = successProbability < .001 ? 8192
                : successProbability >= 1 ? 128
                : (int)Math.Min(8192, Math.Ceiling(Math.Log(.001) / Math.Log(1 - successProbability)));
            maximumIterations = Math.Min(maximumIterations, Math.Max(128, required));
        }
        return best.Count >= 12 && bestMatrix != null
            ? new FundamentalMatrixEstimate { Matrix = bestMatrix, Inliers = best, Iterations = iteration }
            : null;
    }

    private static void Collect(IReadOnlyList<Match> matches, ReadOnlySpan<double> matrix, double squaredThreshold, List<Match> inliers)
    {
        inliers.Clear();
        foreach (var match in matches)
        {
            var leftX = match.A[0];
            var leftY = match.A[1];
            var rightX = match.B[0];
            var rightY = match.B[1];
            var forwardX = matrix[0] * leftX + matrix[1] * leftY + matrix[2];
            var forwardY = matrix[3] * leftX + matrix[4] * leftY + matrix[5];
            var forwardZ = matrix[6] * leftX + matrix[7] * leftY + matrix[8];
            var reverseX = matrix[0] * rightX + matrix[3] * rightY + matrix[6];
            var reverseY = matrix[1] * rightX + matrix[4] * rightY + matrix[7];
            var numerator = rightX * forwardX + rightY * forwardY + forwardZ;
            var denominator = forwardX * forwardX + forwardY * forwardY + reverseX * reverseX + reverseY * reverseY;
            if (denominator > 1e-30 && numerator * numerator < squaredThreshold * denominator)
            {
                inliers.Add(match);
            }
        }
    }

    private static double[]? Fit(IReadOnlyList<Match> matches)
    {
        if (matches.Count < 8)
        {
            return null;
        }
        var left = Normalize(matches, true);
        var right = Normalize(matches, false);
        if (left == null || right == null)
        {
            return null;
        }
        var first = left.Value;
        var second = right.Value;
        // Streaming Givens QR retains the nine-column system without constructing
        // an N by N left singular-vector matrix or squaring its condition number.
        Span<double> triangular = stackalloc double[81];
        Span<double> equation = stackalloc double[9];
        triangular.Clear();
        for (var row = 0; row < matches.Count; row++)
        {
            var match = matches[row];
            var x = (match.A[0] - first.CentreX) * first.Scale;
            var y = (match.A[1] - first.CentreY) * first.Scale;
            var u = (match.B[0] - second.CentreX) * second.Scale;
            var v = (match.B[1] - second.CentreY) * second.Scale;
            equation[0] = u * x;
            equation[1] = u * y;
            equation[2] = u;
            equation[3] = v * x;
            equation[4] = v * y;
            equation[5] = v;
            equation[6] = x;
            equation[7] = y;
            equation[8] = 1;
            AddEquation(triangular, equation);
        }
        var compact = DenseMatrix.Create(9, 9, 0);
        for (var row = 0; row < 9; row++)
        {
            for (var column = row; column < 9; column++)
            {
                compact[row, column] = triangular[row * 9 + column];
            }
        }
        var decomposition = compact.Svd(true);
        if (decomposition.S[7] < decomposition.S[0] * 1e-8)
        {
            return null;
        }
        var vector = decomposition.VT.Row(8);
        Matrix<double> matrix = DenseMatrix.Create(3, 3, (row, column) => vector[row * 3 + column]);
        var singular = matrix.Svd(true);
        matrix = singular.U * DenseMatrix.OfDiagonalArray([singular.S[0], singular.S[1], 0]) * singular.VT;
        return (Transform(second).Transpose() * matrix * Transform(first)).ToRowMajorArray();
    }

    private static void AddEquation(Span<double> triangular, Span<double> equation)
    {
        for (var column = 0; column < 9; column++)
        {
            var diagonal = triangular[column * 9 + column];
            var incoming = equation[column];
            var magnitude = Math.Sqrt(diagonal * diagonal + incoming * incoming);
            if (magnitude < 1e-30)
            {
                continue;
            }
            var cosine = diagonal / magnitude;
            var sine = incoming / magnitude;
            for (var target = column; target < 9; target++)
            {
                var index = column * 9 + target;
                var upper = triangular[index];
                var lower = equation[target];
                triangular[index] = cosine * upper + sine * lower;
                equation[target] = -sine * upper + cosine * lower;
            }
        }
    }

    private static PixelNormalization? Normalize(IReadOnlyList<Match> matches, bool left)
    {
        double centreX = 0;
        double centreY = 0;
        foreach (var match in matches)
        {
            var pixel = left ? match.A : match.B;
            centreX += pixel[0];
            centreY += pixel[1];
        }
        centreX /= matches.Count;
        centreY /= matches.Count;
        double distance = 0;
        foreach (var match in matches)
        {
            var pixel = left ? match.A : match.B;
            var dx = pixel[0] - centreX;
            var dy = pixel[1] - centreY;
            distance += Math.Sqrt(dx * dx + dy * dy);
        }
        return distance > 1e-8 && double.IsFinite(distance)
            ? new PixelNormalization(centreX, centreY, Math.Sqrt(2) * matches.Count / distance)
            : null;
    }

    private static Matrix<double> Transform(PixelNormalization normalization)
    {
        var matrix = DenseMatrix.Create(3, 3, 0);
        matrix[0, 0] = normalization.Scale;
        matrix[0, 2] = -normalization.CentreX * normalization.Scale;
        matrix[1, 1] = normalization.Scale;
        matrix[1, 2] = -normalization.CentreY * normalization.Scale;
        matrix[2, 2] = 1;
        return matrix;
    }
}
