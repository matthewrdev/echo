namespace Echo.Optimization;

internal sealed class Evaluation
{
    public double U
    {
        get; init;
    }
    public double V
    {
        get; init;
    }
    public double[] CameraU { get; init; } = [];
    public double[] CameraV { get; init; } = [];
    public double[] PointU { get; init; } = [];
    public double[] PointV { get; init; } = [];
    public double FocalU
    {
        get; init;
    }
    public double FocalV
    {
        get; init;
    }
}
