using System.Text.Json;
using Echo.Contracts;

namespace Echo.Infrastructure;

public static class DepthMapJsonReader
{
    public static DepthMap Read(ReadOnlySpan<byte> json)
    {
        if (json.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            json = json[3..];
        }

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a depth map object.");
        }

        var map = new DepthMap();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a depth map property.");
            }

            var name = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Truncated depth map.");
            }

            if (string.Equals(name, "imageId", StringComparison.OrdinalIgnoreCase))
            {
                map.ImageId = reader.GetString() ?? "";
            }
            else if (string.Equals(name, "width", StringComparison.OrdinalIgnoreCase))
            {
                map.Width = reader.GetInt32();
            }
            else if (string.Equals(name, "height", StringComparison.OrdinalIgnoreCase))
            {
                map.Height = reader.GetInt32();
            }
            else if (string.Equals(name, "depth", StringComparison.OrdinalIgnoreCase))
            {
                map.Depth = ReadSamples(ref reader, Capacity(map, json.Length));
            }
            else if (string.Equals(name, "confidence", StringComparison.OrdinalIgnoreCase))
            {
                map.Confidence = ReadSamples(ref reader, Capacity(map, json.Length));
            }
            else
            {
                reader.Skip();
            }
        }

        var dimensions = (long)map.Width * map.Height;
        var validDimensions = map.Width > 0 && map.Height > 0 && dimensions == map.Depth.Length;
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            throw new JsonException("Truncated depth map or trailing JSON.");
        }
        if (!validDimensions || map.Confidence.Length != map.Depth.Length)
        {
            throw new JsonException("Depth dimensions do not match the saved samples.");
        }

        return map;
    }

    private static int Capacity(DepthMap map, int jsonLength)
    {
        var size = (long)map.Width * map.Height;
        var validSize = map.Width > 0 && map.Height > 0 && size <= jsonLength;
        return validSize ? (int)size : 0;
    }

    private static float[] ReadSamples(ref Utf8JsonReader reader, int expected)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected depth samples.");
        }

        var samples = expected > 0 ? new float[expected] : null;
        var unordered = samples == null ? new List<float>() : null;
        var count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var sample = reader.GetSingle();
            if (!float.IsFinite(sample))
            {
                throw new JsonException("Non-finite depth sample.");
            }
            if (samples != null)
            {
                if (count >= samples.Length)
                {
                    throw new JsonException("Too many depth samples.");
                }
                samples[count] = sample;
            }
            else
            {
                unordered!.Add(sample);
            }
            count++;
        }

        if (reader.TokenType != JsonTokenType.EndArray || (samples != null && count != samples.Length))
        {
            throw new JsonException("Incomplete depth samples.");
        }

        return samples ?? unordered!.ToArray();
    }
}
