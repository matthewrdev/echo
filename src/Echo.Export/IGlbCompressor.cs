namespace Echo.Export;

public interface IGlbCompressor
{
    int Compress(string source, string destination, int? jpegQuality = null);
}
