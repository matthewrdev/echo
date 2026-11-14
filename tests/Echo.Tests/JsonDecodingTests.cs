using System.ComponentModel.Composition;
using System.Text;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Texturing;

namespace Echo.Tests;

[Export]
public sealed class JsonDecodingTests
{
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public JsonDecodingTests(IJsonCodec json)
    {
        this.json = json;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Generated JSON contracts preserve persisted values and serialization", () =>
        {
            RoundTrip(new ProjectData());
            RoundTrip(new RunData());
            RoundTrip(new Artifact());
            RoundTrip(new Settings());
            RoundTrip(new Photo());
            RoundTrip(new List<Hint> { new Hint() });
            RoundTrip(new FeatureSet { ImageId = "photo", Features = [new Feature { X = 1.25, Y = 2.5, Descriptor = [0.1f, 0.2f] }] });
            RoundTrip(new ImagePair());
            RoundTrip(new ReconstructionScene());
            RoundTrip(new DenseCloudReport());
            RoundTrip(new FusedVolume());
            RoundTrip(new Surface());
            RoundTrip(new TextureResult { MeshFile = "mesh.json", Uvs = [[0.25, 0.75]], Size = 256 });
            RoundTrip(new Dictionary<string, string> { ["image"] = "artifact/path" });
            RoundTrip(new List<string> { "artifact/path" });
        });
        tests.Run("Span depth reader preserves float bits and out-of-order contracts", () =>
        {
            var random = new Random(41);
            var samples = new List<float> { 0, -0f, float.Epsilon, float.MinValue, float.MaxValue };
            for (var i = 0; i < 10000; i++)
            {
                var value = BitConverter.Int32BitsToSingle((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1));
                if (float.IsFinite(value))
                {
                    samples.Add(value);
                }
            }
            var map = new DepthMap { ImageId = "photo\u0020one", Width = samples.Count, Height = 1, Depth = samples.ToArray(), Confidence = samples.ToArray() };
            RoundTrip(map);
            var array = JsonSerializer.Serialize(samples);
            var reordered = Encoding.UTF8.GetBytes($$"""{"CONFIDENCE":{{array}},"unknown":{"list":[null,true,false]},"DEPTH":{{array}},"HEIGHT":1,"WIDTH":{{samples.Count}},"IMAGEID":"photo\u0020one"}""");
            var actual = DepthMapJsonReader.Read(reordered);
            Check(actual.ImageId == map.ImageId, "Escaped image ID");
            Check(actual.Depth.Select(BitConverter.SingleToInt32Bits).SequenceEqual(samples.Select(BitConverter.SingleToInt32Bits)), "Depth float bits");
            Check(actual.Confidence.Select(BitConverter.SingleToInt32Bits).SequenceEqual(samples.Select(BitConverter.SingleToInt32Bits)), "Confidence float bits");
        });
        tests.Run("Span depth reader rejects truncated, oversized and malformed samples", () =>
        {
            const string valid = """{"imageId":"p","width":1,"height":1,"depth":[1.25e-3],"confidence":[1]}""";
            var bytes = Encoding.UTF8.GetBytes(valid);
            for (var length = 0; length < bytes.Length; length++)
            {
                Reject(bytes[..length]);
            }
            foreach (var number in new[] { "01", "+1", "NaN", "1e", "1e999", "null", "true", "\"1\"", "0x123" })
            {
                Reject(Encoding.UTF8.GetBytes(valid.Replace("1.25e-3", number)));
            }
            Reject(Encoding.UTF8.GetBytes(valid.Replace("\"width\":1", "\"width\":2147483647")));
            Reject(Encoding.UTF8.GetBytes(valid.Replace("\"height\":1", "\"height\":-1")));
            Reject(Encoding.UTF8.GetBytes(valid.Replace("[1]", "[]")));
            Reject(Encoding.UTF8.GetBytes(valid + "{}"));
        });
    }

    private void RoundTrip<T>(T value)
    {
        var encoded = json.Encode(value);
        var expected = JsonSerializer.Deserialize<T>(encoded, json.Options)!;
        var actual = Read<T>(encoded);
        Check(json.Encode(actual) == json.Encode(expected), $"Generated {typeof(T).Name} changed the saved contract: {json.Encode(actual)} != {json.Encode(expected)}");
    }

    private T Read<T>(string encoded)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-json-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, encoded);
            return json.Read<T>(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void Reject(byte[] bytes)
    {
        try
        {
            DepthMapJsonReader.Read(bytes);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return;
        }
        throw new InvalidOperationException("Malformed depth JSON was accepted.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
