namespace Echo.Rendering.Previews;

internal readonly record struct DepthRange(float Near, float Far)
{
    public float Normalize(float depth) => Far > Near ? (depth - Near) / (Far - Near) : 0.5f;
}
