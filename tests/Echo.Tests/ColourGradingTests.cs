using Echo.Contracts;
using Echo.Imaging;
using SkiaSharp;

namespace Echo.Tests;

public sealed class ColourGradingTests
{
    public ColourGradingTests()
    {
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Colour grading preserves original bytes at identity and validates recipes", () =>
        {
            foreach (var type in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
            {
                using var bitmap = new SKBitmap(256, 1, type, SKAlphaType.Unpremul);
                bitmap.Pixels = Enumerable.Range(0, 256).Select(i => new SKColor((byte)i, (byte)(255 - i), 37, (byte)i)).ToArray();
                var bytes = bitmap.GetPixelSpan().ToArray();
                new ColourGradeTransform(null).Apply(bitmap, default);
                Check(bytes.SequenceEqual(bitmap.GetPixelSpan().ToArray()), "Identity must preserve every channel and alpha byte.");
                Expect<OperationCanceledException>(() => new ColourGradeTransform(null).Apply(bitmap, new CancellationToken(true)));
            }
            foreach (var grade in new[] { new ColourGrade { Version = 2 }, new ColourGrade { Exposure = double.NaN },
                new ColourGrade { BlackPoint = .5 }, new ColourGrade { WhitePoint = .5 }, new ColourGrade { Saturation = -1 },
                new ColourGrade { Temperature = double.PositiveInfinity }, new ColourGrade { Vibrance = 2 } })
            {
                Expect<EchoException>(() => new Settings { ColourGrade = grade }.Validate());
            }
        });
        tests.Run("Colour grade applies linear-light exposure, neutral desaturation, balance and bounded levels", () =>
        {
            var exposed = new ColourGradeTransform(new ColourGrade { Exposure = 1 }).Apply(new SKColor(128, 128, 128, 91));
            Check(exposed == new SKColor(176, 176, 176, 91), "One stop doubles linear light and preserves alpha.");
            var neutral = new ColourGradeTransform(new ColourGrade { Saturation = 0 }).Apply(new SKColor(220, 80, 30));
            Check(neutral == new SKColor(106, 106, 106), "Zero saturation follows encoded RGB luminance.");
            var warm = new ColourGradeTransform(new ColourGrade { Temperature = 1 }).Apply(new SKColor(128, 128, 128));
            Check(warm == new SKColor(150, 128, 109), "Positive temperature warms neutral colour without changing green.");
            var levels = new ColourGradeTransform(new ColourGrade { BlackPoint = .1, WhitePoint = .9 });
            Check(levels.Apply(new SKColor(10, 10, 10)) == SKColors.Black, "Black point maps low values to black.");
            Check(levels.Apply(new SKColor(245, 245, 245)) == SKColors.White, "White point maps high values to white.");
        });
        tests.Run("Colour grading bitmap channels match individual colours and preserve premultiplied alpha", () =>
        {
            var grade = new ColourGradeTransform(new ColourGrade
            {
                Exposure = .7,
                Temperature = -.3,
                Tint = .2,
                Shadows = .4,
                Highlights = -.2,
                Contrast = 1.1,
                Vibrance = .3,
                Saturation = .8
            });
            foreach (var type in new[] { SKColorType.Bgra8888, SKColorType.Rgba8888 })
            {
                using var bitmap = new SKBitmap(2, 1, type, SKAlphaType.Unpremul);
                var source = new SKColor(50, 100, 150, 127);
                bitmap.SetPixel(0, 0, source);
                bitmap.SetPixel(1, 0, new SKColor(20, 30, 40, 0));
                var transparent = bitmap.GetPixel(1, 0);
                grade.Apply(bitmap, default);
                Check(bitmap.GetPixel(0, 0) == grade.Apply(source), "RGBA and BGRA must match the scalar grading path.");
                Check(bitmap.GetPixel(1, 0) == transparent, "Transparent pixels remain unchanged.");
            }
            using var premultiplied = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
            premultiplied.SetPixel(0, 0, new SKColor(100, 80, 60, 128));
            var expected = grade.Apply(premultiplied.GetPixel(0, 0));
            grade.Apply(premultiplied, default);
            var actual = premultiplied.GetPixel(0, 0);
            Check(actual.Alpha == 128 && Math.Abs(actual.Red - expected.Red) <= 2
                && Math.Abs(actual.Green - expected.Green) <= 2 && Math.Abs(actual.Blue - expected.Blue) <= 2,
                "Premultiplied colour is graded after unpremultiplying, then restored.");
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
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
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
