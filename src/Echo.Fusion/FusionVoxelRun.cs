namespace Echo.Fusion;

internal readonly record struct FusionVoxelRun(int X, int Y, int Z, int Start, int Count)
{
    public long End => (long)X + Count - 1;
}
