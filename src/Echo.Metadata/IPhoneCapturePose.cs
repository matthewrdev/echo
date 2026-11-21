using System.Text.Json;
using Echo.Models;

namespace Echo.Metadata;

public static class IPhoneCapturePose
{
    public static Attitude? Estimate(Dictionary<string, JsonElement> metadata)
    {
        var isIPhone = string.Equals(Text(metadata, "Make"), "Apple", StringComparison.OrdinalIgnoreCase)
            && Text(metadata, "Model")?.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase) == true;
        if (!isIPhone || !IsRearCamera(metadata) || Text(metadata, "GPSImgDirectionRef") != "T")
        {
            return null;
        }

        var yaw = Number(metadata, "GPSImgDirection");
        var orientation = Number(metadata, "Orientation");
        var hasSupportedOrientation = orientation is 1 or 3 or 6 or 8;
        if (yaw is not (>= 0 and <= 360) || !hasSupportedOrientation)
        {
            return null;
        }

        if (!metadata.TryGetValue("AccelerationVector", out var vector)
            || vector.ValueKind != JsonValueKind.Array || vector.GetArrayLength() != 3)
        {
            return null;
        }

        var x = Number(vector[0]);
        var y = Number(vector[1]);
        var z = Number(vector[2]);
        if (x is not double appleX || y is not double appleY || z is not double appleZ)
        {
            return null;
        }

        var inPlane = Math.Sqrt(appleX * appleX + appleY * appleY);
        var magnitude = Math.Sqrt(inPlane * inPlane + appleZ * appleZ);
        // Treat the acceleration as gravity only near one g. A near-vertical view has no stable azimuth.
        if (magnitude is < .8 or > 1.2 || inPlane < .1)
        {
            return null;
        }

        // Rear-camera gravity in raw image coordinates is (-Y, -X, -Z).
        // Apply the same EXIF rotation as the pixels before measuring roll.
        var roll = orientation switch
        {
            3 => Math.Atan2(appleY, appleX),
            6 => Math.Atan2(appleX, -appleY),
            8 => Math.Atan2(-appleX, appleY),
            _ => Math.Atan2(-appleY, -appleX)
        };
        return new Attitude
        {
            Yaw = yaw.Value % 360,
            Pitch = Math.Atan2(appleZ, inPlane) * 180 / Math.PI,
            Roll = roll * 180 / Math.PI,
            Source = "iPhone sensor estimate"
        };
    }

    private static bool IsRearCamera(Dictionary<string, JsonElement> metadata)
    {
        if (metadata.TryGetValue("FrontFacingCamera", out var front))
        {
            return front.ValueKind == JsonValueKind.False || Number(front) == 0;
        }

        return Text(metadata, "LensModel")?.Contains(" back ", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? Text(Dictionary<string, JsonElement> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? Number(Dictionary<string, JsonElement> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) ? Number(value) : null;
    }

    private static double? Number(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;
    }
}
