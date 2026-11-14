using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Optimization;

namespace Echo.Tests;

[Export]
public sealed class Matrix3InverterTests
{
    [ImportingConstructor]
    public Matrix3InverterTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Point Hessian inversion is independent of scale and rejects singular systems", () =>
        {
            double[] basis = [4, 1, .2, 1, 3, .5, .2, .5, 2];
            var matrix = new double[9];
            var inverse = new double[9];
            foreach (var scale in new[] { 1e-12, 1e-6, 1.0, 1e6, 1e12 })
            {
                for (var index = 0; index < matrix.Length; index++)
                {
                    matrix[index] = basis[index] * scale;
                }
                Matrix3Inverter.Invert(matrix, inverse);
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        double product = 0;
                        for (var inner = 0; inner < 3; inner++)
                        {
                            product += matrix[row * 3 + inner] * inverse[inner * 3 + column];
                        }
                        if (Math.Abs(product - (row == column ? 1 : 0)) > 1e-10)
                        {
                            throw new InvalidOperationException("Scaled matrix inverse did not reconstruct identity");
                        }
                    }
                }
            }
            try
            {
                Matrix3Inverter.Invert(new double[9], inverse);
                throw new InvalidOperationException("A singular matrix must be rejected");
            }
            catch (EchoException)
            {
            }
        });
    }
}
