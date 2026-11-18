using System.Globalization;
using Echo.Contracts;

namespace Echo.Cli;

public static class SubjectDistanceRunOptionParser
{
    public static SubjectDistanceRunOptions? Parse(Arguments args)
    {
        var value = args.Get("subject-distance", null);
        if (value == null)
        {
            return null;
        }

        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return new SubjectDistanceRunOptions { Mode = SubjectDistanceMode.Auto };
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return new SubjectDistanceRunOptions { Mode = SubjectDistanceMode.Off };
        }

        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum);
        var valid = parsed && double.IsFinite(maximum) && maximum > 0;
        if (!valid)
        {
            throw new EchoException("--subject-distance expects auto, off, or a positive maximum distance in metres.", 2);
        }

        return new SubjectDistanceRunOptions { Mode = SubjectDistanceMode.Manual, MaximumDistance = maximum };
    }
}
