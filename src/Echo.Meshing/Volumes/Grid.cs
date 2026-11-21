namespace Echo.Meshing.Volumes;

internal readonly record struct Grid(int X, int Y, int Z)
{
    public Grid Add(int x, int y, int z) => new(X + x, Y + y, Z + z);
}
