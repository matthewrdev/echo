namespace Echo.Export;

public sealed record MappingGrid(double West, double South, int Width, int Height, double Resolution)
{
    public double East => West + Width * Resolution;
    public double North => South + Height * Resolution;
}
