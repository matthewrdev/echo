using System.Text.Json;

namespace Echo.Infrastructure;

public interface IJsonCodec
{
    JsonSerializerOptions Options
    {
        get;
    }

    string Encode<T>(T value);
    void Write<T>(Stream stream, T value);
    void WriteGenerated<T>(Stream stream, T value);
    T Read<T>(string path);
    T Clone<T>(T value);
}
