using System.Globalization;
using Echo.Models;

namespace Echo.Cli.Commands.Processing;

public static class TextureRunOptionParser
{
    public static TextureRunOptions? Parse(Arguments args)
    {
        var colour = args.Get("colour-tuning", null);
        var seams = args.Get("edge-softening", null);
        if (colour == null && seams == null)
        {
            return null;
        }
        return new TextureRunOptions
        {
            ColourStrength = colour == null ? null : Colour(colour),
            SeamWidth = seams == null ? null : Seams(seams)
        };
    }

    private static double Colour(string value)
    {
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return .5;
        }
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var strength);
        var valid = parsed && double.IsFinite(strength) && strength is >= 0 and <= 1;
        return valid ? strength : throw new EchoException("--colour-tuning expects on, off, or a strength from 0 to 1.", 2);
    }

    private static int Seams(string value)
    {
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        var parsed = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width);
        return parsed && width is >= 0 and <= 16 ? width
            : throw new EchoException("--edge-softening expects on, off, or an atlas strip width from 0 to 16 pixels.", 2);
    }
}
