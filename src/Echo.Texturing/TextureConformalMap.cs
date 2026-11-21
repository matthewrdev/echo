using Echo.Models;
using Echo.Compute;

namespace Echo.Texturing;

internal static class TextureConformalMap
{
    // Levy et al., Least Squares Conformal Maps (SIGGRAPH 2002).
    // Minimise the area-weighted Cauchy-Riemann residual, fixing two boundary
    // vertices to remove translation/rotation/scale. Validate the resulting map
    // separately: a least-squares solve alone is not an overlap guarantee.
    public static double[]? Solve(TextureChartMesh chart, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        if (chart.Boundary.Length < 3)
        {
            return null;
        }
        var points = chart.Points;
        var first = chart.Boundary.MaxBy(vertex => (points[vertex] - points[chart.Boundary[0]]).Length);
        var second = chart.Boundary.MaxBy(vertex => (points[vertex] - points[first]).Length);
        var length = (points[first] - points[second]).Length;
        if (!(length > 0))
        {
            return null;
        }
        var system = new TextureLinearSystem(points.Length * 2);
        Span<int> indices = stackalloc int[6];
        Span<double> real = stackalloc double[6];
        Span<double> imaginary = stackalloc double[6];
        foreach (var triangle in chart.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            var edge = points[triangle[1]] - points[triangle[0]];
            var other = points[triangle[2]] - points[triangle[0]];
            var edgeLength = edge.Length;
            var area = edge.Cross(other).Length;
            if (!(area > 0) || !(edgeLength > 0))
            {
                return null;
            }
            var x = edge.Dot(other) / edgeLength;
            var y = area / edgeLength;
            var weight = 1 / Math.Sqrt(area);
            real[0] = (x - edgeLength) * weight;
            real[1] = -y * weight;
            real[2] = -x * weight;
            real[3] = y * weight;
            real[4] = edgeLength * weight;
            real[5] = 0;
            for (var corner = 0; corner < 3; corner++)
            {
                indices[corner * 2] = triangle[corner] * 2;
                indices[corner * 2 + 1] = triangle[corner] * 2 + 1;
                imaginary[corner * 2] = -real[corner * 2 + 1];
                imaginary[corner * 2 + 1] = real[corner * 2];
            }
            for (var row = 0; row < 6; row++)
            {
                for (var column = 0; column < 6; column++)
                {
                    system.Add(indices[row], indices[column], real[row] * real[column] + imaginary[row] * imaginary[column]);
                }
            }
        }
        return system.Solve(new Dictionary<int, double>
        {
            [first * 2] = 0,
            [first * 2 + 1] = 0,
            [second * 2] = length,
            [second * 2 + 1] = 0
        }, cancellation, compute, backend);
    }
}
