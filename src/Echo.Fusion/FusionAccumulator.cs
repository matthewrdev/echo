using Echo.Geometry;

namespace Echo.Fusion;

internal struct FusionAccumulator
{
    public double Sum
    {
        get; private set;
    }
    public double Weight
    {
        get; private set;
    }
    public int Support
    {
        get; private set;
    }

    public void Add(DepthObservation observation, double focal, double tolerance, double voxel)
    {
        if (!observation.IsValid)
        {
            return;
        }

        var distance = observation.Distance;
        var uncertainty = FusionDepthUncertainty.Radius(observation.CameraDepth, focal, tolerance);
        var truncation = Math.Max(voxel * 3, uncertainty * 2);
        var precision = 1 / (uncertainty * uncertainty);
        if (distance < -truncation)
        {
            return;
        }

        if (distance > truncation)
        {
            if (observation.HasFullCoverage)
            {
                Sum += truncation * .5 * precision;
                Weight += .5 * precision;
            }

            return;
        }

        var weight = (1 - .5 * Math.Abs(distance) / truncation) * precision;
        Sum += distance * weight;
        Weight += weight;
        Support++;
    }
}
