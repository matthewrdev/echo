using System.Text;

namespace Echo.Metadata;

// DJI-DBG APP7 packets contain [name:value] records. Binary value layouts are
// vendor-private; expose the original bytes, without inventing sample types.
public static class DjiDebugMetadata
{
    public static void Decode(ReadOnlySpan<byte> packet, PhotoMetadata result)
    {
        if (!packet.StartsWith("DJI-DBG\0"u8))
        {
            return;
        }

        var remaining = packet[8..];
        while (!remaining.IsEmpty && remaining[0] == '[')
        {
            var end = FindEnd(remaining);
            if (end < 0)
            {
                result.Add("DJI", "Warning", "Truncated DJI debug metadata record.");
                return;
            }

            var record = remaining[1..end];
            var colon = record.IndexOf((byte)':');
            if (colon > 0 && colon <= 128 && IsText(record[..colon]))
            {
                var name = FieldName(Encoding.ASCII.GetString(record[..colon]));
                var value = record[(colon + 1)..];
                var text = value.TrimEnd((byte)0);
                if (!text.IsEmpty && IsText(text))
                {
                    result.Add("DJI", name, Encoding.ASCII.GetString(text));
                }
                else
                {
                    result.Add("DJI", name, value.ToArray());
                }
            }

            remaining = remaining[(end + 1)..];
        }
    }

    private static int FindEnd(ReadOnlySpan<byte> record)
    {
        for (var index = 1; index < record.Length; index++)
        {
            if (record[index] == ']' && (index + 1 == record.Length || record[index + 1] == '['))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsText(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item < 32 || item > 126)
            {
                return false;
            }
        }

        return true;
    }

    private static string FieldName(string name)
    {
        return name switch
        {
            "ae_dbg_info" => "AEDebugInfo",
            "ae_histogram_info" => "AEHistogramInfo",
            "ae_local_histogram" => "AELocalHistogram",
            "ae_liveview_histogram_info" => "AELiveViewHistogramInfo",
            "ae_liveview_local_histogram" => "AELiveViewLocalHistogram",
            "awb_dbg_info" => "AWBDebugInfo",
            "af_dbg_info" => "AFDebugInfo",
            "hiso" => "Histogram",
            "xidiri" => "Xidiri",
            "GimbalDegree(Y,P,R)" => "GimbalDegree",
            "FlightDegree(Y,P,R)" => "FlightDegree",
            "FlightSpeed(X,Y,Z)" => "FlightSpeed",
            "adj_dbg_info" => "ADJDebugInfo",
            "sensor_id" => "SensorID",
            "hyperlapse_dbg_info" => "HyperlapsDebugInfo",
            _ => name
        };
    }
}
