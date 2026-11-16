using System.Globalization;
using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.Xmp;
using MetadataDirectory = MetadataExtractor.Directory;

namespace Echo.Metadata;

public static class MetadataFields
{
    public static void Add(MetadataDirectory directory, PhotoMetadata result)
    {
        if (directory is XmpDirectory xmp)
        {
            foreach (var property in xmp.GetXmpProperties())
            {
                var colon = property.Key.IndexOf(':');
                var prefix = colon >= 0 ? property.Key[..colon] : "other";
                var name = colon >= 0 ? property.Key[(colon + 1)..] : property.Key;
                // These numeric fields feed camera priors. Other XMP strings
                // (including identifiers with leading zeroes) retain their type.
                var numeric = prefix == "drone-dji" && IsDjiNumber(name);
                if (numeric && double.TryParse(property.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && double.IsFinite(number))
                {
                    result.Add("XMP-" + prefix, name, number);
                }
                else
                {
                    result.Add("XMP-" + prefix, name, property.Value);
                }
            }
        }
        else
        {
            var group = Group(directory);
            foreach (var tag in directory.Tags)
            {
                var name = Name(directory, tag.Type, tag.Name);
                var value = directory.GetObject(tag.Type);
                result.Add(group, name, name == "ExifVersion" && value is byte[] version ? Encoding.ASCII.GetString(version) : Value(value));
            }
            if (directory is GpsDirectory gps)
            {
                AddGps(gps, result);
            }
        }
        if (directory.HasError)
        {
            result.Add(Group(directory), "ReaderWarnings", string.Join("; ", directory.Errors));
        }
    }

    private static void AddGps(GpsDirectory gps, PhotoMetadata result)
    {
        if (gps.TryGetGeoLocation(out var location))
        {
            var finite = double.IsFinite(location.Latitude) && double.IsFinite(location.Longitude);
            if (finite && Math.Abs(location.Latitude) <= 90 && Math.Abs(location.Longitude) <= 180)
            {
                result.Add("GPS", "GPSLatitude", location.Latitude);
                result.Add("GPS", "GPSLongitude", location.Longitude);
            }
        }
        if (gps.GetObject(GpsDirectory.TagAltitude) is Rational rational && double.IsFinite(rational.ToDouble()))
        {
            var belowReference = gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out var reference) && reference is 1 or 3;
            result.Add("GPS", "GPSAltitude", belowReference ? -rational.ToDouble() : rational.ToDouble());
        }
    }

    private static bool IsDjiNumber(string name)
    {
        return name is "AbsoluteAltitude" or "RelativeAltitude" or "GpsLatitude" or "GpsLongitude"
            or "FlightPitchDegree" or "FlightRollDegree" or "FlightYawDegree"
            or "FlightXSpeed" or "FlightYSpeed" or "FlightZSpeed"
            or "GimbalPitchDegree" or "GimbalRollDegree" or "GimbalYawDegree";
    }

    private static string Group(MetadataDirectory directory)
    {
        return directory switch
        {
            ExifIfd0Directory => "IFD0",
            ExifSubIfdDirectory => "ExifIFD",
            ExifThumbnailDirectory => "IFD1",
            DjiMakernoteDirectory => "DJI",
            _ => Compact(directory.Name)
        };
    }

    private static string Name(MetadataDirectory directory, int tag, string name)
    {
        if (directory is ExifDirectoryBase && directory is not GpsDirectory)
        {
            return tag switch
            {
                ExifDirectoryBase.Tag35MMFilmEquivFocalLength => "FocalLengthIn35mmFormat",
                ExifDirectoryBase.TagDateTimeOriginal => "DateTimeOriginal",
                ExifDirectoryBase.TagDateTimeDigitized => "CreateDate",
                ExifDirectoryBase.TagDateTime => "ModifyDate",
                ExifDirectoryBase.TagFNumber => "FNumber",
                ExifDirectoryBase.TagIsoEquivalent => "ISO",
                _ => Compact(name)
            };
        }
        if (!directory.HasTagName(tag))
        {
            return Compact(directory.Name.Replace(" Makernote", "")) + "_0x" + tag.ToString("x4", CultureInfo.InvariantCulture);
        }
        return Compact(name);
    }

    private static string Compact(string text)
    {
        var name = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                name.Append(character);
            }
        }
        return name.ToString();
    }

    private static object? Value(object? value)
    {
        switch (value)
        {
            case Rational rational:
                return double.IsFinite(rational.ToDouble()) ? rational.ToDouble() : rational.ToString();
            case StringValue text:
                return text.ToString();
            case Rational[] rationals:
                var numbers = new object[rationals.Length];
                for (var index = 0; index < numbers.Length; index++)
                {
                    numbers[index] = Value(rationals[index])!;
                }
                return numbers;
            case byte[]:
            case string:
            case null:
                return value;
            case Array array:
                var items = new object?[array.Length];
                for (var index = 0; index < items.Length; index++)
                {
                    items[index] = Value(array.GetValue(index));
                }
                return items;
            case double number when !double.IsFinite(number):
            case float single when !float.IsFinite(single):
                return value.ToString();
            case IConvertible:
                return value;
            default:
                return value.ToString();
        }
    }
}
