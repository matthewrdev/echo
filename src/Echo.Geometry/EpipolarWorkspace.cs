using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Geometry;

internal sealed class EpipolarWorkspace
{
    public DenseVector First { get; } = new(3);
    public DenseVector Second { get; } = new(3);
    public DenseVector Forward { get; } = new(3);
    public DenseVector Reverse { get; } = new(3);
}
