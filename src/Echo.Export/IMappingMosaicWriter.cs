namespace Echo.Export;

public interface IMappingMosaicWriter
{
    void Write(IReadOnlyList<string> sources, string destination, double maximumSeamErrorMetres);
}
