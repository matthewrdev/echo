using System.Security.Cryptography;
using System.Text;

namespace Echo.Compute.Metal;

internal sealed class MetalComputeLibrary
{
    private readonly IReadOnlyDictionary<ComputeOperation, string> implementations;

    public MetalComputeLibrary(IReadOnlyDictionary<string, string> sources)
    {
        Source = string.Join("\n", sources.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value));
        implementations = Enum.GetValues<ComputeOperation>().Where(operation => operation != ComputeOperation.TexturePacking).ToDictionary(operation => operation,
            operation => Fingerprint(sources, operation));
    }

    public string Source
    {
        get;
    }

    public string Implementation(ComputeOperation operation) => implementations[operation];

    private string Fingerprint(IReadOnlyDictionary<string, string> sources, ComputeOperation operation)
    {
        string[] files = operation switch
        {
            ComputeOperation.Features => ["00Common.metal", "03Gaussian.metal"],
            ComputeOperation.Matching => ["00Common.metal", "01Descriptors.metal", "07Epipolar.metal"],
            ComputeOperation.Alignment => ["00Common.metal", "01Descriptors.metal", "02DepthViews.metal", "07Epipolar.metal", "07Triangulation.metal"],
            ComputeOperation.Depth => ["00Common.metal", "03DepthFilter.metal"],
            ComputeOperation.Cloud => ["00Common.metal", "02DepthViews.metal", "03Cloud.metal"],
            ComputeOperation.Fusion => ["00Common.metal", "02DepthViews.metal", "03Fusion.metal"],
            ComputeOperation.Mesh => ["00Common.metal", "02DepthViews.metal", "04Surface.metal"],
            ComputeOperation.Texture => ["00Common.metal", "05Seams.metal", "06Texture.metal", "08UvSolve.metal"],
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var source = string.Join("\n", files.Select(file => file + "\n" + sources[file]));
        return "echo-compute-metal-1;safe;" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
