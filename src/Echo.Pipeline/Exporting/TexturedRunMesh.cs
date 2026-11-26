using Echo.Models;

namespace Echo.Pipeline.Exporting;

public sealed record TexturedRunMesh(Surface Surface, IList<double[]> Uvs, string Atlas);
