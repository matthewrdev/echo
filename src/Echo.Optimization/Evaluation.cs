namespace Echo.Optimization;

internal sealed class Evaluation
{
    public double U
    {
        get; set;
    }
    public double V
    {
        get; set;
    }
    public double[] CameraU { get; } = new double[6];
    public double[] CameraV { get; } = new double[6];
    public double[] PointU { get; } = new double[3];
    public double[] PointV { get; } = new double[3];
    public double FocalU
    {
        get; set;
    }
    public double FocalV
    {
        get; set;
    }
}
