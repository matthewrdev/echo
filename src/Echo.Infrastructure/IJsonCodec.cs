using System.Text.Json;

namespace Echo.Infrastructure;

public interface IJsonCodec
{
    JsonSerializerOptions Options
    {
        get;
    }

    string Encode<T>(T value);
    T Read<T>(string path);
    T Clone<T>(T value);
}
