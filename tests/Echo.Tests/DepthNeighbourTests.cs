using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Geometry;

namespace Echo.Tests;

[Export]
public sealed class DepthNeighbourTests
{
    [ImportingConstructor]
    public DepthNeighbourTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Bounded stereo selection preserves stable ordering, baseline fallback and limits", () =>
        {
            var random = new Random(53);
            var reference = new CameraPose { ImageId = "reference" };
            foreach (var count in new[] { 0, 1, 2, 12, 193, 1000 })
            {
                var scene = new ReconstructionScene { Cameras = [reference] };
                for (var index = 0; index < count; index++)
                {
                    scene.Cameras.Add(new CameraPose
                    {
                        ImageId = index.ToString(),
                        Centre = [random.Next(-10, 10), random.Next(-10, 10), 0],
                        Rotation = index % 7 == 0 ? [-1, 0, 0, 0, 1, 0, 0, 0, -1] : [1, 0, 0, 0, 1, 0, 0, 0, 1]
                    });
                }
                foreach (var limit in new[] { 0, 1, 2, 12, 2000 })
                {
                    foreach (var median in new[] { 1.0, 20, 200 })
                    {
                        var candidates = scene.Cameras.Where(camera => camera.ImageId != reference.ImageId
                            && new V3(camera.Centre).Length > .05
                            && CameraGeometry.Rotate(camera.Rotation, new V3(0, 0, 1)).Z > .8).ToList();
                        var preferred = candidates.Where(camera => new V3(camera.Centre).Length >= Math.Clamp(median * .06, .75, 3)).ToList();
                        var expected = (preferred.Count < 2 ? candidates : preferred).OrderBy(camera => new V3(camera.Centre).Length).Take(limit).ToArray();
                        var actual = DepthNeighbourSelector.Select(scene, reference, median, limit, default);
                        if (!expected.SequenceEqual(actual))
                        {
                            throw new InvalidOperationException($"Neighbour selection changed for {count} cameras, limit {limit}, median {median}.");
                        }
                    }
                }
            }
            var fallback = new ReconstructionScene
            {
                Cameras = [reference,
                new CameraPose { ImageId = "near", Centre = [1, 0, 0] },
                new CameraPose { ImageId = "far", Centre = [4, 0, 0] }]
            };
            if (DepthNeighbourSelector.Select(fallback, reference, 100, 1, default)[0].ImageId != "near")
            {
                throw new InvalidOperationException("One strong-baseline candidate must still fall back to the closer neighbour.");
            }
            fallback.Cameras.Add(new CameraPose { ImageId = "farther", Centre = [5, 0, 0] });
            if (DepthNeighbourSelector.Select(fallback, reference, 100, 1, default)[0].ImageId != "far")
            {
                throw new InvalidOperationException("Two strong-baseline candidates must use preferred selection even with limit one.");
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                DepthNeighbourSelector.Select(fallback, reference, 100, 12, cancellation.Token);
                throw new InvalidOperationException("Selection ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }
}
