using Echo.Models;
using Echo.Texturing.Layout;

namespace Echo.Pipeline.Texture;

public interface ITextureLayoutStore
{
    void Write(string path, TextureLayout layout, CancellationToken cancellation);
    TextureLayout Read(string path, Surface mesh, CancellationToken cancellation);
}
