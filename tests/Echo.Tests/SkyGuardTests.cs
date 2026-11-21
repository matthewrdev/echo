using Echo.Texturing.Rasterization;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

public sealed class SkyGuardTests
{
    public SkyGuardTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Sky guard excludes blue and overcast skyline colours without recolouring the foreground", () =>
        {
            foreach (var sky in new[] { new SKColor(100, 170, 230), new SKColor(220, 225, 230) })
            {
                var pixels = Picture(sky);
                var guard = TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 4, default);
                Check(guard != null, "A top-connected smooth sky is detected");
                var edge = TextureSampling.Bilinear(pixels, 128, 128, 64, 62.5, guard);
                Check(edge == new SKColor(80, 110, 60), "The sky-side boundary uses foreground colour");
                Check(TextureSampling.Bilinear(pixels, 128, 128, 64, 100, guard) == pixels[100 * 128 + 64], "Interior texture remains exact");
                Check(TextureSampling.Bilinear(pixels, 128, 128, 64, 0, guard) == sky, "Large projection errors are not hidden by unlimited stretching");
                Check(TextureSampling.Bilinear(pixels, 128, 128, 64, 62.5) == sky, "Disabled sampling is unchanged");
            }
        });
        tests.Run("Sky guard respects isolated blue objects, image channel order and cancellation", () =>
        {
            var pixels = Picture(new SKColor(100, 170, 230));
            pixels[100 * 128 + 64] = new SKColor(90, 130, 240);
            var guard = TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 4, default);
            Check(TextureSampling.Bilinear(pixels, 128, 128, 64, 100, guard) == pixels[100 * 128 + 64], "Blue foreground is not globally desaturated");
            var swapped = pixels.Select(pixel => new SKColor(pixel.Blue, pixel.Green, pixel.Red)).ToArray();
            var other = TextureSkyGuardBuilder.Build(swapped, 128, 128, true, 4, default);
            Check(guard!.Adjust(64, 63) == other!.Adjust(64, 63), "Mask geometry is independent of native channel layout");
            Check(TextureSkyGuardBuilder.Build(Enumerable.Repeat(SKColors.White, 128 * 128).ToArray(), 128, 128, false, 4, default) == null,
                "An all-background photo cannot invent foreground colour");
            Array.Fill(pixels, new SKColor(80, 110, 60));
            Check(TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 4, default) == null, "No detected sky leaves the photo unchanged");
            for (var index = 0; index < pixels.Length; index++)
            {
                var grey = (byte)((index % 128 + index / 128) % 2 == 0 ? 195 : 155);
                pixels[index] = new SKColor(grey, grey, grey);
            }
            Check(TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 4, default) == null, "Pale textured rock touching the top edge is not a sky seed");
            Expect<OperationCanceledException>(() => TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 4, new CancellationToken(true)));
            Expect<ArgumentOutOfRangeException>(() => TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 0, default));
        });
        tests.Run("Sky margins move samples farther into the foreground without touching mesh geometry", () =>
        {
            var pixels = Picture(new SKColor(210, 220, 230));
            var narrow = TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 2, default)!;
            var wide = TextureSkyGuardBuilder.Build(pixels, 128, 128, false, 8, default)!;
            Check(wide.Adjust(64, 63).Y > narrow.Adjust(64, 63).Y, "The margin is a real sampling control");
        });
    }

    private SKColor[] Picture(SKColor sky)
    {
        return Enumerable.Range(0, 128 * 128).Select(index => index / 128 < 64 ? sky : new SKColor(80, 110, 60)).ToArray();
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
