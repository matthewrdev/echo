using Echo.Contracts;

namespace Echo.Export;

public interface IGisWriter
{
    void Write(string directory, ReconstructionScene scene, Surface? surface, IReadOnlyList<Photo> photos);
}
