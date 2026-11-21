using Echo.Models;
using Echo.Geometry;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class TextureParameterizationTests
{
    private readonly ITexturePatchBuilder builder;
    private readonly ITextureLayoutPlanner planner;

    public TextureParameterizationTests(ITexturePatchBuilder builder, ITextureLayoutPlanner planner)
    {
        this.builder = builder;
        this.planner = planner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Conformal charts unwrap a deeply curved strip without camera or local-normal seams", () =>
        {
            var mesh = Strip(48, 4, 1);
            var geometry = new TextureChartGeometry(mesh, default);
            var patches = builder.Build(new ProjectData(), new ReconstructionScene(), mesh, default);
            Check(patches.Count == 1, "A developable curved surface should have one chart");
            Check(patches[0].Faces.Count == mesh.Faces.Count, "Every face survives");
            Check(patches[0].Coordinates.Count == mesh.Vertices.Count, "Shared vertices remain shared within a chart");
            Validate(geometry, patches);
        });
        tests.Run("Chart validation rejects foldovers, global boundary overlap and collapsed texel density", () =>
        {
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                Faces = [[0, 1, 2], [0, 2, 3]],
                SourceImages = ["", ""]
            };
            var chart = new TextureChartMesh(new TextureChartGeometry(mesh, default), [0, 1]);
            Check(TextureChartValidation.Accept(chart, [0, 0, 1, 0, 1, 1, 0, 1], default), "Unit square is valid");
            Check(TextureChartValidation.Accept(chart, [10000, 20000, 10001, 20000, 10001, 20001, 10000, 20001], default), "Boundary checks are translation invariant");
            Check(!TextureChartValidation.Accept(chart, [0, 0, 1, 0, 1, 1, 1, 0], default), "Foldover is rejected");
            Check(!TextureChartValidation.Accept(chart, [0, 0, 100, 0, 100, .01, 0, .01], default), "Equal-area severe anisotropy is rejected");

            mesh.Faces[1] = [3, 2, 0];
            var inconsistent = new TextureChartMesh(new TextureChartGeometry(mesh, default), [0, 1]);
            Check(inconsistent.Boundary.Length == 0, "Inconsistent winding cannot pass the oriented-disk proof");

            var wrapped = Strip(48, 1, 1);
            for (var vertex = 0; vertex < wrapped.Vertices.Count; vertex++)
            {
                wrapped.Vertices[vertex] = [vertex % 49, vertex / 49, 0];
            }
            var wrappedChart = new TextureChartMesh(new TextureChartGeometry(wrapped, default), Enumerable.Range(0, wrapped.Faces.Count).ToList());
            var overlap = wrappedChart.Points.SelectMany(point => new[]
            {
                (1 + point.Y) * Math.Cos(3 * Math.PI * point.X / 48),
                -(1 + point.Y) * Math.Sin(3 * Math.PI * point.X / 48)
            }).ToArray();
            Check(!TextureChartValidation.HasSimpleBoundary(wrappedChart, overlap, default), "Overlapping boundary is rejected even when local triangles can be positive");
        });
        tests.Run("Conformal chart construction is invariant to model units and honours cancellation", () =>
        {
            foreach (var scale in new[] { 1e-4, 1e4 })
            {
                var mesh = Strip(16, 3, scale);
                var patches = builder.Build(new ProjectData(), new ReconstructionScene(), mesh, default);
                Check(patches.Count == 1, "Changing world units should not introduce seams");
                Validate(new TextureChartGeometry(mesh, default), patches);
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                builder.Build(new ProjectData(), new ReconstructionScene(), Strip(8, 2, 1), cancellation.Token);
                throw new Exception("Cancellation was ignored");
            }
            catch (OperationCanceledException)
            {
            }
        });
        tests.Run("Atlas family reserves whole-pixel padding at 2K and scales its layout exactly", () =>
        {
            var project = new ProjectData { Settings = new Settings { TextureSize = 8192, TextureGutter = 5, TextureChartFaces = 32 } };
            var mesh = Strip(16, 3, 1);
            var layout = planner.Plan(project, new ReconstructionScene(), mesh, default);
            Check(layout.MinimumAtlasSize == 2048 && layout.GutterPixels == 8, "Round padding up to two whole pixels at 2K");
            foreach (var patch in layout.Patches)
            {
                Check(patch.AtlasBounds.Left >= 8 && patch.AtlasBounds.Top >= 8, "Padding survives at atlas edges");
                Check(patch.AtlasBounds.Right <= 8192 - 8 && patch.AtlasBounds.Bottom <= 8192 - 8, "Chart remains inside padded bounds");
            }
            Check(layout.Patches.Sum(patch => patch.Faces.Count) == mesh.Faces.Count, "Resolution planning never removes faces");
        });
        tests.Run("Atlas reductions use linear-light area filtering without introducing colour-channel swaps", () =>
        {
            using var source = new SKBitmap(4, 4);
            source.Erase(SKColors.Black);
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    source.SetPixel(x, y, x < 2 ? (y % 2 == 0 ? SKColors.Red : SKColors.Black) : SKColors.Blue);
                }
            }
            using var reduced = TextureAtlasDownsampler.Half(source, default);
            var red = reduced.GetPixel(0, 0);
            var blue = reduced.GetPixel(1, 0);
            Check(red.Red is >= 187 and <= 189 && red.Blue == 0, "Half red intensity is filtered in linear light");
            Check(blue.Blue == 255 && blue.Red == 0, "Separate blue region stays blue");
            Check(red.Alpha == 255 && blue.Alpha == 255, "Texture stays opaque");
        });
    }

    private Surface Strip(int columns, int rows, double scale, double angle = 1.8 * Math.PI)
    {
        var mesh = new Surface();
        for (var y = 0; y <= rows; y++)
        {
            for (var x = 0; x <= columns; x++)
            {
                var theta = angle * x / columns;
                mesh.Vertices.Add([scale * Math.Cos(theta), scale * y / rows, scale * Math.Sin(theta)]);
            }
        }
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var a = y * (columns + 1) + x;
                var b = a + 1;
                var c = a + columns + 1;
                var d = c + 1;
                mesh.Faces.Add([a, b, d]);
                mesh.Faces.Add([a, d, c]);
                mesh.SourceImages.Add(x % 2 == 0 ? "a" : "b");
                mesh.SourceImages.Add(x % 2 == 0 ? "b" : "a");
            }
        }
        return mesh;
    }

    private void Validate(TextureChartGeometry geometry, IReadOnlyList<TexturePatch> patches)
    {
        var faces = patches.SelectMany(patch => patch.Faces).ToArray();
        Check(faces.Length == geometry.Surface.Faces.Count && faces.Distinct().Count() == faces.Length, "Exactly one UV assignment per face");
        foreach (var patch in patches)
        {
            var chart = new TextureChartMesh(geometry, patch.Faces);
            var coordinates = new double[chart.Points.Length * 2];
            foreach (var entry in patch.Coordinates)
            {
                var local = chart.LocalVertices[geometry.WeldedVertices[entry.Key]];
                coordinates[local * 2] = entry.Value[0];
                coordinates[local * 2 + 1] = entry.Value[1];
            }
            Check(TextureChartValidation.Accept(chart, coordinates, default), "Chart has finite, non-overlapping, bounded-stretch UVs");
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
