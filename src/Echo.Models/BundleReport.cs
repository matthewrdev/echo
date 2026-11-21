namespace Echo.Models;

public sealed class BundleReport
{
    public int Tracks
    {
        get; set;
    }
    public int Observations
    {
        get; set;
    }
    public int AcceptedIterations
    {
        get; set;
    }
    public double InitialMedianError
    {
        get; set;
    }
    public double FinalUntrimmedMedianError
    {
        get; set;
    }
    public double FinalMedianError
    {
        get; set;
    }
    public string ErrorUnit { get; set; } = "pixels at 1000-pixel image width";
    public double MaximumCameraMovementMetres
    {
        get; set;
    }
}
