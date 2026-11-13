namespace Echo.Contracts;

public sealed class AlignmentRecoveryAssessment
{
    public bool Accepted
    {
        get; set;
    }
    public int EstablishedObservations
    {
        get; set;
    }
    public int RetainedObservations
    {
        get; set;
    }
    public int ProposedObservations
    {
        get; set;
    }
    public string Reason { get; set; } = "";
}
