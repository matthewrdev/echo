namespace Echo.Texturing;

public sealed record TextureProfile(int[] Top, int[] Bottom, int Height, int QuarterTurns)
{
    public int Width => Top.Length;
}
