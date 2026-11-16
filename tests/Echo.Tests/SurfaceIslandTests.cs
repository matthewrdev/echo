using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Meshing;

namespace Echo.Tests;

[Export]
public sealed class SurfaceIslandTests
{
    [ImportingConstructor]
    public SurfaceIslandTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Island trimming uses surface area rather than adaptive triangle density", () =>
        {
            var source = new Surface();
            AddGrid(source, 10, 1, 0, "main");
            AddGrid(source, 1, 10, 20, "noise");
            AddGrid(source, 4, 1, 30, "rock");
            var small = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Small, 2, default);
            Check(small.InputComponents == 3 && small.RemovedComponents == 1, "Only the small component is removed");
            Check(small.RemovedFaces == 200 && Math.Abs(small.RemovedArea - 1) < 1e-10, "Dense noise is measured by area");
            Check(small.Surface.SourceImages.SequenceEqual(new[] { "main", "main", "rock", "rock" }), "Larger detached objects survive small-island mode");
            var main = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, 2, default);
            Check(main.Surface.Faces.Count == 2 && main.Surface.SourceImages.All(image => image == "main"), "The largest area wins even with fewer triangles");
            Check(main.RemovedComponents == 2 && source.Faces.Count == 204, "Trimming does not mutate its source");
        });
        tests.Run("Island connectivity crosses photo seams and preserves inferred colour assignments", () =>
        {
            var source = new Surface
            {
                Vertices = [[0, 0, 0], [4, 0, 0], [4, 4, 0], [0, 0, 0], [0, 4, 0], [4, 4, 0],
                    [10, 0, 0], [11, 0, 0], [10, 1, 0], [999, 999, 999]],
                Faces = [[0, 1, 2], [3, 4, 5], [6, 7, 8]],
                SourceImages = ["photo", "", "noise"]
            };
            var result = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, .1, default);
            Check(result.InputComponents == 2 && result.RemovedFaces == 1, "Duplicated seam vertices do not create false islands");
            Check(result.Surface.Vertices.Count == 6 && result.Surface.SourceImages.SequenceEqual(new[] { "photo", "" }), "Seams and unobserved-face markers survive compaction");
            for (var index = 0; index < 2; index++)
            {
                var actual = result.Surface.Faces[index];
                for (var corner = 0; corner < 3; corner++)
                {
                    Check(result.Surface.Vertices[actual[corner]].SequenceEqual(source.Vertices[source.Faces[index][corner]]), "Retained geometry and winding are unchanged");
                }
            }
        });
        tests.Run("Whole-island removal leaves the completed main component closed", () =>
        {
            var source = new Surface
            {
                Vertices = [[0, 0, 0], [10, 0, 0], [0, 10, 0], [0, 0, 10],
                    [20, 0, 0], [21, 0, 0], [20, 1, 0], [20, 0, 1]],
                Faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3],
                    [4, 6, 5], [4, 5, 7], [4, 7, 6], [5, 6, 7]]
            };
            var result = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Small, 2, default);
            Check(result.RemovedComponents == 1 && result.Surface.Faces.Count == 4, "A whole closed floater is removed");
            Check(SurfaceBoundaryLoops.Find(result.Surface, default).Count() == 0, "No closed holes are reopened");
            Check(result.Surface.SourceImages.Count == 0, "Unassigned geometry remains unassigned");
        });
        tests.Run("Island trimming retains attached geometry even across nonmanifold edges", () =>
        {
            var source = new Surface
            {
                Vertices = [[0, 0, 0], [10, 0, 0], [0, 10, 0], [0, -10, 0], [0, 0, .001]],
                Faces = [[0, 1, 2], [1, 0, 3], [0, 1, 4]]
            };
            var result = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, 100, default);
            Check(result.InputComponents == 1 && ReferenceEquals(result.Surface, source), "A small attached protrusion is not a disconnected island");
        });
        tests.Run("Island thresholds preserve equality and main mode breaks equal-area ties deterministically", () =>
        {
            var source = new Surface();
            AddGrid(source, 1, 1, 0, "first");
            AddGrid(source, 1, 2, 10, "second");
            Check(SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Small, 100, default).RemovedComponents == 0, "Components exactly at the threshold survive");
            Check(SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, 0, default).Surface.SourceImages.All(image => image == "first"), "Equal area uses the first component, not polygon count");
        });
        tests.Run("Island trimming is optional, handles empty meshes and honours validation and cancellation", () =>
        {
            var source = new Surface();
            AddGrid(source, 1, 1, 0, "photo");
            AddGrid(source, .1, 1, 10, "noise");
            Check(ReferenceEquals(SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Off, 100, default).Surface, source), "Off retains the entire original mesh");
            Check(SurfaceIslandTrimmer.Trim(new Surface(), IslandTrimMode.Main, .1, default).InputComponents == 0, "An empty mesh has no islands");
            Expect<ArgumentOutOfRangeException>(() => SurfaceIslandTrimmer.Trim(source, (IslandTrimMode)999, .1, default));
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 101 })
            {
                Expect<ArgumentOutOfRangeException>(() => SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Small, value, default));
            }
            Expect<OperationCanceledException>(() => SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, .1, new CancellationToken(true)));
            source.SourceImages.RemoveAt(0);
            Expect<ArgumentException>(() => SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Small, .1, default));
        });
    }

    private void AddGrid(Surface source, double size, int divisions, double offset, string image)
    {
        var start = source.Vertices.Count;
        for (var y = 0; y <= divisions; y++)
        {
            for (var x = 0; x <= divisions; x++)
            {
                source.Vertices.Add([offset + size * x / divisions, size * y / divisions, 0]);
            }
        }
        for (var y = 0; y < divisions; y++)
        {
            for (var x = 0; x < divisions; x++)
            {
                var a = start + y * (divisions + 1) + x;
                source.Faces.Add([a, a + 1, a + divisions + 2]);
                source.Faces.Add([a, a + divisions + 2, a + divisions + 1]);
                source.SourceImages.Add(image);
                source.SourceImages.Add(image);
            }
        }
    }

    private void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
