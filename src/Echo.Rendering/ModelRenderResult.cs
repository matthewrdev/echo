namespace Echo.Rendering;

public sealed record ModelRenderResult(string Input, string Output, int Width, int Height, string View,
    string Mode, int Vertices, int Triangles)
{
    public string? CameraImageId
    {
        get; init;
    }
}
