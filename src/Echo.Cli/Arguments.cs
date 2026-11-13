using System.Globalization;
using Echo.Contracts;

public sealed class Arguments
{
    public List<string> Words { get; } = [];
    public Dictionary<string, string?> Options { get; } = [];

    public string? Get(string key, string? fallback) => Options.GetValueOrDefault(key, fallback);

    public string Word(int index, string fallback) => index < Words.Count ? Words[index] : fallback;

    public string RequiredWord(int index) => index < Words.Count ? Words[index] : throw new EchoException("Missing command argument. Use --help.", 2);

    public int Int(string key, int fallback)
    {
        var value = Get(key, null);
        var result = value == null ? fallback : int.Parse(value, CultureInfo.InvariantCulture);
        return result > 0 ? result : throw new EchoException("Expected a positive integer.", 2);
    }

    public double Number(string key) => double.Parse(Get(key, null) ?? throw new EchoException($"--{key} is required.", 2), CultureInfo.InvariantCulture);

    public bool IsNdjson => Get("events", "") == "ndjson";
}
