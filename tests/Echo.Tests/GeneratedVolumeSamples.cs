using System.Collections;
using Echo.Meshing.Volumes;
using Echo.Meshing;

namespace Echo.Tests;

// More than four million samples without a matching managed dictionary.
internal sealed class GeneratedVolumeSamples : IReadOnlyDictionary<Grid, float>
{
    private const int side = 162;

    public int Count => side * side * side;

    public IEnumerable<Grid> Keys
    {
        get
        {
            for (var x = 0; x < side; x++)
            {
                for (var y = 0; y < side; y++)
                {
                    for (var z = 0; z < side; z++)
                    {
                        yield return new Grid(x - 20000001, y + 20000001, z);
                    }
                }
            }
        }
    }

    public IEnumerable<float> Values => Keys.Select(key => this[key]);
    public float this[Grid key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
    public bool ContainsKey(Grid key) => TryGetValue(key, out _);

    public bool TryGetValue(Grid key, out float value)
    {
        var x = key.X + 20000001;
        var y = key.Y - 20000001;
        var inside = (uint)x < side && (uint)y < side && (uint)key.Z < side;
        value = inside ? (x - y) * .125f + key.Z : 0;
        return inside;
    }

    public IEnumerator<KeyValuePair<Grid, float>> GetEnumerator()
    {
        foreach (var key in Keys)
        {
            yield return new KeyValuePair<Grid, float>(key, this[key]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
