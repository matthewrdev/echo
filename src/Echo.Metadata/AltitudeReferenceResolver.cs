using System.Globalization;
using System.Text.Json;
using Echo.Contracts;

namespace Echo.Metadata;

public static class AltitudeReferenceResolver
{
    public static AltitudeReference Resolve(GeoPosition position, IReadOnlyDictionary<string, JsonElement> metadata)
    {
        if (!position.Altitude.HasValue)
        {
            return new AltitudeReference { Evidence = "No source altitude was recorded." };
        }
        var absolute = Number(metadata, "AbsoluteAltitude");
        var gps = Number(metadata, "GPSAltitude");
        var matchesAbsolute = Matches(position.Altitude.Value, absolute);
        var matchesGps = Matches(position.Altitude.Value, gps);
        var source = matchesAbsolute ? "AbsoluteAltitude" : matchesGps ? "GPSAltitude" : "supplied altitude";
        if (!string.IsNullOrWhiteSpace(position.AltitudeDatum) && position.AltitudeDatum != "unknown")
        {
            return new AltitudeReference { Datum = position.AltitudeDatum, Source = source, Evidence = "Reference supplied with the geographic position." };
        }
        var result = new AltitudeReference { Source = source, Evidence = "The source does not identify a vertical reference." };
        if (!matchesGps)
        {
            result.Evidence = matchesAbsolute
                ? "Vendor AbsoluteAltitude has no confirmed vertical reference; GPSAltitude cannot establish it because the values differ or GPSAltitude is absent."
                : result.Evidence;
            return result;
        }
        var reference = Number(metadata, "GPSAltitudeRef");
        var version = Text(metadata, "ExifVersion");
        var modernExif = int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var exifVersion) && exifVersion >= 300;
        // Exif 3.0 corrected the old ambiguous sea-level definition of values 0/1.
        // https://www.cipa.jp/std/documents/e/Exif3.0-Overview_E.pdf
        if (!modernExif)
        {
            result.Evidence = $"GPSAltitudeRef={reference?.ToString(CultureInfo.InvariantCulture) ?? "absent"}, ExifVersion={version ?? "absent"}. Legacy EXIF does not reliably distinguish ellipsoidal height from sea-level height.";
            return result;
        }
        var mapDatum = Text(metadata, "GPSMapDatum")?.Replace("-", "").Replace(" ", "");
        if (reference is 0 or 1 && string.Equals(mapDatum, "WGS84", StringComparison.OrdinalIgnoreCase))
        {
            result.Datum = "WGS84 ellipsoid";
            result.Evidence = "Inferred from EXIF 3.x GPSAltitudeRef=0/1 and GPSMapDatum=WGS84; source altitude matches GPSAltitude.";
        }
        else if (reference is 2 or 3)
        {
            result.Datum = "mean sea level (geoid unspecified)";
            result.Evidence = "EXIF 3.x GPSAltitudeRef=2/3 declares sea-level height but does not identify a geoid model.";
        }
        return result;
    }

    public static double? Number(IReadOnlyDictionary<string, JsonElement> metadata, string name)
    {
        var text = Text(metadata, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
    }

    private static string? Text(IReadOnlyDictionary<string, JsonElement> metadata, string name)
    {
        if (!metadata.TryGetValue(name, out var value))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool Matches(double altitude, double? candidate) => candidate.HasValue && Math.Abs(altitude - candidate.Value) < 0.001;
}
