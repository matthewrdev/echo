using Echo.Models;

namespace Echo.Cli.Commands.Hints;

public sealed class HintFactory : IHintFactory
{
    public HintFactory()
    {
    }

    public Hint Create(Arguments args, string action)
    {
        var hint = new Hint
        {
            Type = action,
            ImageId = args.RequiredWord(2)
        };
        if (action == "camera")
        {
            if (args.Options.ContainsKey("lat") || args.Options.ContainsKey("lon"))
            {
                hint.Position = new GeoPosition
                {
                    Latitude = args.Number("lat"),
                    Longitude = args.Number("lon"),
                    Altitude = args.Options.ContainsKey("alt") ? args.Number("alt") : null,
                    AltitudeDatum = args.Get("alt-datum", "unknown") ?? "unknown",
                    UncertaintyMetres = args.Options.ContainsKey("uncertainty") ? args.Number("uncertainty") : 10
                };
            }

            if (args.Options.ContainsKey("vertical-uncertainty"))
            {
                if (!args.Options.ContainsKey("uncertainty"))
                {
                    throw new EchoException("Specify horizontal --uncertainty when supplying --vertical-uncertainty.", 2);
                }

                hint.VerticalUncertaintyMetres = args.Number("vertical-uncertainty");
            }

            if (args.Options.ContainsKey("yaw") || args.Options.ContainsKey("pitch"))
            {
                hint.Attitude = new Attitude
                {
                    Yaw = args.Number("yaw"),
                    Pitch = args.Number("pitch"),
                    Roll = args.Options.ContainsKey("roll") ? args.Number("roll") : 0
                };
            }
        }
        else if (action is "overlap" or "tie")
        {
            hint.OtherImageId = args.RequiredWord(3);
            if (action == "tie")
            {
                hint.Pixel = [args.Number("ax"), args.Number("ay")];
                hint.OtherPixel = [args.Number("bx"), args.Number("by")];
            }
        }
        else
        {
            throw new EchoException("Unknown hint action.", 2);
        }

        return hint;
    }
}
