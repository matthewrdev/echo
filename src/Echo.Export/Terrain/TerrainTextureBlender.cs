using SkiaSharp;

namespace Echo.Export.Terrain;

internal sealed class TerrainTextureBlender
{
    public void Blend(SKBitmap terrain, TerrainGrid grid, TerrainWall wall, int offset, int[] ring, CancellationToken cancellation)
    {
        using var source = SKBitmap.Decode(wall.Texture);
        for (var index = 0; index < ring.Length; index += 3)
        {
            cancellation.ThrowIfCancellationRequested();
            var corners = ring.AsSpan(index, 3);
            var points = new double[3][];
            var scanUvs = new double[3][];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = corners[corner];
                if (vertex < offset)
                {
                    points[corner] = grid.Uv(vertex % grid.Columns, vertex / grid.Columns);
                    scanUvs[corner] = [];
                }
                else
                {
                    var wallVertex = vertex - offset;
                    var pixel = wall.GridPositions[wallVertex];
                    points[corner] = grid.Uv(pixel[0], pixel[1]);
                    scanUvs[corner] = [wall.Uvs[wallVertex * 2], wall.Uvs[wallVertex * 2 + 1]];
                }
            }

            Paint(terrain, source, points, scanUvs);
        }
    }

    private static void Paint(SKBitmap terrain, SKBitmap scan, double[][] points, double[][] scanUvs)
    {
        var left = Math.Max(0, (int)Math.Floor(points.Min(point => point[0]) * terrain.Width));
        var right = Math.Min(terrain.Width - 1, (int)Math.Ceiling(points.Max(point => point[0]) * terrain.Width));
        var top = Math.Max(0, (int)Math.Floor(points.Min(point => point[1]) * terrain.Height));
        var bottom = Math.Min(terrain.Height - 1, (int)Math.Ceiling(points.Max(point => point[1]) * terrain.Height));
        var a = points[0];
        var b = points[1];
        var c = points[2];
        var determinant = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var u = (x + .5) / terrain.Width - a[0];
                var v = (y + .5) / terrain.Height - a[1];
                var w1 = (u * (c[1] - a[1]) - v * (c[0] - a[0])) / determinant;
                var w2 = ((b[0] - a[0]) * v - (b[1] - a[1]) * u) / determinant;
                var w0 = 1 - w1 - w2;
                if (Math.Min(w0, Math.Min(w1, w2)) >= 0)
                {
                    PaintPixel(terrain, scan, scanUvs, x, y, w0, w1, w2);
                }
            }
        }
    }

    private static void PaintPixel(SKBitmap terrain, SKBitmap scan, double[][] scanUvs, int x, int y, double a, double b, double c)
    {
        Span<double> weights = [a, b, c];
        var weight = 0d;
        var u = 0d;
        var v = 0d;
        for (var index = 0; index < weights.Length; index++)
        {
            if (scanUvs[index].Length != 0)
            {
                weight += weights[index];
                u += weights[index] * scanUvs[index][0];
                v += weights[index] * scanUvs[index][1];
            }
        }

        if (weight <= 1e-8)
        {
            return;
        }

        var px = Math.Clamp((int)(u / weight * scan.Width), 0, scan.Width - 1);
        var py = Math.Clamp((int)(v / weight * scan.Height), 0, scan.Height - 1);
        var source = scan.GetPixel(px, py);
        var ground = terrain.GetPixel(x, y);
        var mix = weight * weight * (3 - 2 * weight);
        byte Blend(byte original, byte detail) => (byte)Math.Clamp(Math.Round(original + (detail - original) * mix), 0, 255);
        terrain.SetPixel(x, y, new SKColor(Blend(ground.Red, source.Red), Blend(ground.Green, source.Green), Blend(ground.Blue, source.Blue)));
    }
}
