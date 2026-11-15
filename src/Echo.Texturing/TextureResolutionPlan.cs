namespace Echo.Texturing;

internal sealed class TextureResolutionPlan
{
    public TextureResolutionPlan(int size, int gutter)
    {
        MinimumSize = Math.Min(2048, size);
        Scale = (double)size / MinimumSize;
        MinimumGutter = Math.Max(1, (int)Math.Ceiling(gutter / Scale));
        Gutter = (int)(MinimumGutter * Scale);
    }

    public int MinimumSize
    {
        get;
    }
    public int MinimumGutter
    {
        get;
    }
    public int Gutter
    {
        get;
    }
    public double Scale
    {
        get;
    }
}
