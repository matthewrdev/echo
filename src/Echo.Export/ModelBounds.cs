using Echo.Models;

namespace Echo.Export;

public sealed class ModelBounds
{
    public double[] Minimum { get; } = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
    public double[] Maximum { get; } = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];

    public ModelBounds(Surface surface)
    {
        if (surface.Vertices.Count == 0)
        {
            throw new EchoException("The model contains no vertices.", 2);
        }
        foreach (var vertex in surface.Vertices)
        {
            if (vertex.Length != 3 || vertex.Any(value => !double.IsFinite(value)))
            {
                throw new EchoException("The model contains invalid coordinates.", 2);
            }
            for (var axis = 0; axis < 3; axis++)
            {
                Minimum[axis] = Math.Min(Minimum[axis], vertex[axis]);
                Maximum[axis] = Math.Max(Maximum[axis], vertex[axis]);
            }
        }
    }

    public double Centre(int axis) => (Minimum[axis] + Maximum[axis]) / 2;
    public double HalfExtent(int axis) => (Maximum[axis] - Minimum[axis]) / 2;
}
