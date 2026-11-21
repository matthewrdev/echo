using System.Runtime.InteropServices;
using Echo.Models;

namespace Echo.Matching;

internal static class ContiguousDescriptors
{
    public static ReadOnlyMemory<float> Find<T>(ReadOnlySpan<T> features, int length) where T : IFeatureDescriptor
    {
        if (features.IsEmpty || !MemoryMarshal.TryGetArray(features[0].DescriptorData, out var first))
        {
            return default;
        }
        for (var index = 0; index < features.Length; index++)
        {
            var available = MemoryMarshal.TryGetArray(features[index].DescriptorData, out var item)
                && ReferenceEquals(item.Array, first.Array) && item.Offset == first.Offset + index * length;
            if (!available || item.Count != length)
            {
                return default;
            }
        }
        return first.Array.AsMemory(first.Offset, checked(features.Length * length));
    }
}
