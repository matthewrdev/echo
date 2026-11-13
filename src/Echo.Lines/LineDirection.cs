using Echo.Contracts;

namespace Echo.Lines;

internal readonly struct LineDirection
{
    public LineDirection(LineFeature line)
    {
        X = line.End[0] - line.Start[0];
        Y = line.End[1] - line.Start[1];
        Length = Math.Sqrt(X * X + Y * Y);
    }

    public double X
    {
        get;
    }
    public double Y
    {
        get;
    }
    public double Length
    {
        get;
    }
}
