namespace Echo.Optimization;

internal struct BundleCrossTerm
{
    public BundleCrossTerm(int key)
    {
        Key = key;
    }

    public int Key
    {
        get;
    }

    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public double Z
    {
        get; set;
    }
}
