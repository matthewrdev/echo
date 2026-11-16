using System.Text.Json;

namespace Echo.Export;

internal sealed class GlbBounds
{
    public required JsonElement? Min
    {
        get; init;
    }
    public required JsonElement? Max
    {
        get; init;
    }
}
