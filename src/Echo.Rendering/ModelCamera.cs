using Echo.Models;

namespace Echo.Rendering;

public sealed record ModelCamera(string ImageId, double[] Centre, double[] Rotation, double FocalPixels,
    int PhotoWidth, int PhotoHeight)
{
    public void Validate()
    {
        var validCentre = Centre.Length == 3 && Centre.All(double.IsFinite);
        var validRotation = Rotation.Length == 9 && Rotation.All(double.IsFinite);
        var validFocal = double.IsFinite(FocalPixels) && FocalPixels > 0;
        var validDimensions = PhotoWidth > 0 && PhotoHeight > 0;
        if (!validCentre || !validRotation || !validFocal || !validDimensions)
        {
            throw new EchoException($"Camera {ImageId} has incomplete pose or calibration data.", 2);
        }
    }
}
