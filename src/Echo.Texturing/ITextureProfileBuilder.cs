namespace Echo.Texturing;

public interface ITextureProfileBuilder
{
    TextureProfile Build(TexturePatch patch, double scale, int cellSize, int gutter, int quarterTurns);
}
