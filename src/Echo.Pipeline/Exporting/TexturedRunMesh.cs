using Echo.Models;

namespace Echo.Pipeline.Exporting;

internal sealed record TexturedRunMesh(Surface Surface, IList<double[]> Uvs, string Atlas);
