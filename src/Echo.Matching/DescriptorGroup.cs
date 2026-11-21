using Echo.Models;

namespace Echo.Matching;

internal sealed class DescriptorGroup<T> where T : IFeatureDescriptor
{
    public List<T> Left { get; } = [];
    public List<T> Right { get; } = [];
    public List<int> LeftIndices { get; } = [];
    public List<int> RightIndices { get; } = [];
}
