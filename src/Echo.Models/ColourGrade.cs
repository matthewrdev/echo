using System.Text.Json.Serialization;

namespace Echo.Models;

// A reversible display-referred finish. Source photographs and cloud records remain unchanged.
public sealed class ColourGrade
{
    public int Version { get; set; } = 1;
    public double Exposure { get; set; } = 0;
    public double Temperature { get; set; } = 0;
    public double Tint { get; set; } = 0;
    public double BlackPoint { get; set; } = 0;
    public double WhitePoint { get; set; } = 1;
    public double Contrast { get; set; } = 1;
    public double Shadows { get; set; } = 0;
    public double Highlights { get; set; } = 0;
    public double Saturation { get; set; } = 1;
    public double Vibrance { get; set; } = 0;

    [JsonIgnore]
    public bool IsIdentity => Version == 1 && Exposure == 0 && Temperature == 0 && Tint == 0
        && BlackPoint == 0 && WhitePoint == 1 && Contrast == 1
        && Shadows == 0 && Highlights == 0 && Saturation == 1 && Vibrance == 0;

    public void Validate()
    {
        var invalidBalance = !InRange(Exposure, -4, 4) || !InRange(Temperature, -1, 1) || !InRange(Tint, -1, 1);
        var invalidLevels = !InRange(BlackPoint, 0, .49) || !InRange(WhitePoint, .51, 1) || !InRange(Contrast, .25, 2);
        var invalidTone = !InRange(Shadows, -1, 1) || !InRange(Highlights, -1, 1);
        var invalidColour = !InRange(Saturation, 0, 2) || !InRange(Vibrance, -1, 1);
        if (Version != 1 || invalidBalance || invalidLevels || invalidTone || invalidColour)
        {
            throw new EchoException("Invalid colour grade. Check the grading version and control ranges.", 2);
        }
    }

    private bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;
}
