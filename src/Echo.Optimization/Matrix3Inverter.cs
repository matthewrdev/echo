using Echo.Models;

namespace Echo.Optimization;

public static class Matrix3Inverter
{
    public static void Invert(ReadOnlySpan<double> matrix, Span<double> inverse)
    {
        if (matrix.Length != 9 || inverse.Length != 9)
        {
            throw new ArgumentException("A 3 by 3 matrix requires nine values.");
        }

        double scale = 0;
        for (var index = 0; index < matrix.Length; index++)
        {
            scale = Math.Max(scale, Math.Abs(matrix[index]));
        }

        if (!double.IsFinite(scale) || scale == 0)
        {
            throw new EchoException("Degenerate point geometry.", 5);
        }

        Span<double> normalized = stackalloc double[9];
        for (var index = 0; index < normalized.Length; index++)
        {
            normalized[index] = matrix[index] / scale;
        }

        var determinant = normalized[0] * (normalized[4] * normalized[8] - normalized[5] * normalized[7])
            - normalized[1] * (normalized[3] * normalized[8] - normalized[5] * normalized[6])
            + normalized[2] * (normalized[3] * normalized[7] - normalized[4] * normalized[6]);
        // Singularity is relative to matrix scale. Small, damped Hessian blocks are
        // invertible even when their unscaled determinant is much smaller than 1e-20.
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-15)
        {
            throw new EchoException("Degenerate point geometry.", 5);
        }

        inverse[0] = normalized[4] * normalized[8] - normalized[5] * normalized[7];
        inverse[1] = normalized[2] * normalized[7] - normalized[1] * normalized[8];
        inverse[2] = normalized[1] * normalized[5] - normalized[2] * normalized[4];
        inverse[3] = normalized[5] * normalized[6] - normalized[3] * normalized[8];
        inverse[4] = normalized[0] * normalized[8] - normalized[2] * normalized[6];
        inverse[5] = normalized[2] * normalized[3] - normalized[0] * normalized[5];
        inverse[6] = normalized[3] * normalized[7] - normalized[4] * normalized[6];
        inverse[7] = normalized[1] * normalized[6] - normalized[0] * normalized[7];
        inverse[8] = normalized[0] * normalized[4] - normalized[1] * normalized[3];
        for (var index = 0; index < inverse.Length; index++)
        {
            inverse[index] /= determinant * scale;
        }
    }
}
