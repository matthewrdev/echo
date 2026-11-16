using System.Collections.Concurrent;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;

namespace Echo.Tests;

internal sealed class CountingFeatureJsonCodec : IJsonCodec
{
    private readonly IJsonCodec inner;
    private readonly ConcurrentDictionary<string, int> reads = new(StringComparer.Ordinal);

    public CountingFeatureJsonCodec(IJsonCodec inner)
    {
        this.inner = inner;
    }

    public JsonSerializerOptions Options => inner.Options;
    public void Write<T>(Stream stream, T value) => inner.Write(stream, value);
    public string Encode<T>(T value) => inner.Encode(value);
    public T Clone<T>(T value) => inner.Clone(value);
    public int ReadCount(string path) => reads.GetValueOrDefault(path);
    public int FeatureReadCount => reads.Values.Sum();

    public T Read<T>(string path)
    {
        if (typeof(T) == typeof(FeatureSet))
        {
            reads.AddOrUpdate(path, 1, (_, count) => count + 1);
        }
        return inner.Read<T>(path);
    }
}
