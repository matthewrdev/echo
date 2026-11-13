namespace Echo.Lines;

internal readonly record struct LineRegionFit(double X, double Y, double TangentX, double TangentY,
    double Minimum, double Maximum, double SquaredError, double Weight)
{
    public double Length => Maximum - Minimum;
}
