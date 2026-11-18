namespace Echo.Depth;

public sealed record DepthSearchRange(double Near, double Far, double Median, int SampleCount, string Source)
{
    public int SubjectDistanceRejectedObservations
    {
        get; init;
    }

    public bool OutsideSubjectRange
    {
        get; init;
    }

    public int RejectedObservations
    {
        get; init;
    }
}
