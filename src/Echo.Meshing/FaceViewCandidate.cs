namespace Echo.Meshing;

internal readonly record struct FaceViewCandidate(int View, double Cost, double Density) : IComparable<FaceViewCandidate>
{
    public int CompareTo(FaceViewCandidate other)
    {
        var order = other.Cost.CompareTo(Cost);
        return order == 0 ? View.CompareTo(other.View) : order;
    }
}
