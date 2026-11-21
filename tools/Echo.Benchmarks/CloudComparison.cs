using System.Globalization;
using System.Text;
using Echo.Contracts;
using Echo.Infrastructure;
using SkiaSharp;

namespace Echo.Benchmarks;

public sealed class CloudComparison
{
    private readonly IJsonCodec json;

    public CloudComparison(IJsonCodec json)
    {
        this.json = json;
    }

    public int Run(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: Echo.Benchmarks compare-cloud BEFORE.ply AFTER.ply SCENE.json OUTPUT.png");
            return 2;
        }
        var before = Sample(args[0]);
        var after = Sample(args[1]);
        var scene = json.Read<ReconstructionScene>(args[2]);
        using var bitmap = new SKBitmap(1600, 1320);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 20);
        canvas.Clear(new SKColor(20, 24, 30));
        for (var row = 0; row < 3; row++)
        {
            var horizontal = row == 2 ? 2 : 0;
            var vertical = row == 0 ? 2 : 1;
            var left = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            var bottom = double.PositiveInfinity;
            var top = double.NegativeInfinity;
            foreach (var point in before)
            {
                left = Math.Min(left, Axis(point, horizontal));
                right = Math.Max(right, Axis(point, horizontal));
                bottom = Math.Min(bottom, Axis(point, vertical));
                top = Math.Max(top, Axis(point, vertical));
            }
            var scale = Math.Min(740 / Math.Max(1e-6, right - left), 350 / Math.Max(1e-6, top - bottom));
            for (var column = 0; column < 2; column++)
            {
                var originX = column * 800 + 30;
                var originY = row * 440 + 55;
                paint.Color = SKColors.White;
                canvas.DrawText((column == 0 ? "Before" : "Consistency checks") + " · " + (row == 0 ? "X/Z" : row == 1 ? "X/Y" : "Z/Y"), originX, originY - 20, font, paint);
                foreach (var point in column == 0 ? before : after)
                {
                    paint.Color = new SKColor(point.Red, point.Green, point.Blue, 190);
                    canvas.DrawCircle((float)(originX + (Axis(point, horizontal) - left) * scale),
                        (float)(originY + (top - Axis(point, vertical)) * scale), 1.1f, paint);
                }
                foreach (var camera in scene.Cameras)
                {
                    paint.Color = SKColors.OrangeRed;
                    canvas.DrawCircle((float)(originX + (camera.Centre[horizontal] - left) * scale),
                        (float)(originY + (top - camera.Centre[vertical]) * scale), 2, paint);
                }
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(args[3]);
        encoded.SaveTo(output);
        Console.WriteLine($"Compared {before.Count:N0} and {after.Count:N0} sampled points; orange markers are cameras. Shared bounds and scale per row.");
        return 0;
    }

    private double Axis(DenseSample point, int axis) => axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;

    private List<DenseSample> Sample(string path)
    {
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        var count = 0;
        var line = new StringBuilder();
        while (input.Position < input.Length)
        {
            var next = reader.ReadByte();
            if (next != 10)
            {
                line.Append((char)next);
                continue;
            }
            var text = line.ToString();
            line.Clear();
            if (text.StartsWith("element vertex ", StringComparison.Ordinal))
            {
                count = int.Parse(text[15..], CultureInfo.InvariantCulture);
            }
            if (text == "end_header")
            {
                break;
            }
        }
        var start = input.Position;
        if (input.Length - start != count * 23L)
        {
            throw new EchoException("Expected an Echo binary dense-cloud PLY.");
        }
        var stride = Math.Max(1, (count + 74999) / 75000);
        var samples = new List<DenseSample>(Math.Min(count, 75000));
        for (var i = 0; i < count; i += stride)
        {
            input.Position = start + i * 23L;
            samples.Add(new DenseSample(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadInt32(), reader.ReadSingle()));
        }
        return samples;
    }
}
