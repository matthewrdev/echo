namespace Echo.Fusion;

internal static class FusionDepthUncertainty
{
    public static double Radius(double cameraDepth, double focal, double relativeTolerance)
    {
        return Math.Max(cameraDepth * relativeTolerance, cameraDepth / focal * 2);
    }
}
