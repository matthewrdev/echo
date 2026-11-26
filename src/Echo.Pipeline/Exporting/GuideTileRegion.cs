namespace Echo.Pipeline.Exporting;

internal readonly record struct GuideTileRegion(double Left, double Bottom, double Right, double Top)
{
    private double MiddleX => Left + (Right - Left) * .5;
    private double MiddleY => Bottom + (Top - Bottom) * .5;

    public int Quadrant(double x, double y) => (x >= MiddleX ? 1 : 0) + (y >= MiddleY ? 2 : 0);

    public GuideTileRegion Child(int quadrant) => quadrant switch
    {
        0 => new(Left, Bottom, MiddleX, MiddleY),
        1 => new(MiddleX, Bottom, Right, MiddleY),
        2 => new(Left, MiddleY, MiddleX, Top),
        _ => new(MiddleX, MiddleY, Right, Top)
    };
}
