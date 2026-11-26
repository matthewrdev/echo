namespace Echo.Pipeline.Exporting;

public interface ITerrainStitchService
{
    void Stitch(string manifestFile, string output, CancellationToken cancellation);
}
