using Echo.Models;
using Echo.Depth;

namespace Echo.Tests;

public sealed class DepthSkyFilterTests
{
    public DepthSkyFilterTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Sky depth filtering removes confident clouds, preserves uncertain silhouettes and clears confidence", () =>
        {
            var semantics = new SemanticMap
            {
                ImageId = "sky",
                Width = 4,
                Height = 4,
                Sky = [255, 255, 255, 255, 255, 255, 255, 255, 100, 100, 0, 0, 0, 0, 0, 0]
            };
            var map = new DepthMap
            {
                ImageId = "sky",
                Width = 16,
                Height = 16,
                Depth = Enumerable.Repeat(10f, 256).ToArray(),
                Confidence = Enumerable.Repeat(.9f, 256).ToArray()
            };
            var removed = DepthSkyFilter.Apply(map, semantics, default);
            Check(removed > 0 && removed < 128, "Only the confidently classified sky interior should be removed");
            Check(map.Depth[0] == 0 && map.Confidence[0] == 0, "The image border must not leave a strip of sky depth");
            Check(map.Depth[8 * 16 + 8] == 10 && map.Confidence[8 * 16 + 8] == .9f, "Uncertain skyline geometry must remain unchanged");
            Check(map.Depth[255] == 10, "Foreground remains unchanged");
            Check(DepthSkyFilter.Apply(map, semantics, default) == 0, "Filtering is idempotent");
        });
        tests.Run("Sky filtering refuses missing or mismatched evidence instead of claiming success", () =>
        {
            var map = new DepthMap { ImageId = "image", Width = 1, Height = 1, Depth = [12], Confidence = [.9f] };
            foreach (var semantics in new[]
            {
                new SemanticMap { ImageId = "image", Width = 1, Height = 1 },
                new SemanticMap { ImageId = "different", Width = 1, Height = 1, Sky = [255] }
            })
            {
                try
                {
                    DepthSkyFilter.Apply(map, semantics, default);
                    throw new InvalidOperationException("Invalid evidence was accepted");
                }
                catch (ArgumentException)
                {
                    Check(map.Depth[0] == 12, "Invalid evidence must not erase geometry");
                }
            }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
