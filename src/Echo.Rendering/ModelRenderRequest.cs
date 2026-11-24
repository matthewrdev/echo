using Echo.Models;

namespace Echo.Rendering;

public sealed record ModelRenderRequest(string Input, string Output, int Width = 1600, int Height = 1000,
    string View = "front", string Mode = "textured")
{
    public bool RawMesh
    {
        get; init;
    }

    public ModelCamera? Camera
    {
        get; init;
    }

    public void Validate()
    {
        var invalidDimensions = Width < 1 || Height < 1 || Width > 8192 || Height > 8192
            || (long)Width * Height > 16_777_216;
        if (invalidDimensions)
        {
            throw new EchoException("Render dimensions must be 1–8192 pixels per side, with at most 16,777,216 pixels total.", 2);
        }

        if (View is not ("front" or "back" or "left" or "right" or "top" or "oblique"))
        {
            throw new EchoException("--view must be front, back, left, right, top or oblique.", 2);
        }

        if (Mode is not ("textured" or "solid" or "wireframe" or "normals"))
        {
            throw new EchoException("--mode must be textured, solid, wireframe or normals.", 2);
        }

        if (RawMesh && Mode == "textured")
        {
            throw new EchoException("Mesh preview mode must be solid, wireframe or normals.", 2);
        }

        Camera?.Validate();

        var extension = RawMesh ? ".ply" : ".glb";
        if (!string.Equals(Path.GetExtension(Input), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(Input))
        {
            throw new EchoException(RawMesh ? "The saved mesh PLY is missing." : "Render input must be an existing Echo-exported GLB file.", 2);
        }

        if (!string.Equals(Path.GetExtension(Output), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new EchoException("--output must name a PNG file.", 2);
        }

        if (Path.Exists(Output))
        {
            throw new EchoException("Render output already exists. Choose a new PNG path.", 2);
        }
    }
}
