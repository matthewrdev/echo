using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Lines;
using Echo.Matching;

namespace Echo.Tests;

[Export]
public sealed class LineFeatureTests
{
    private readonly ILineDetector detector;
    private readonly IFeatureMatcher matcher;

    [ImportingConstructor]
    public LineFeatureTests(ILineDetector detector, IFeatureMatcher matcher)
    {
        this.detector = detector;
        this.matcher = matcher;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Line detector recovers a straight edge and rejects a blank image", () =>
        {
            var gray = new float[256 * 192];
            Check(detector.Detect(gray, 256, 192, 100, 30, default).Count == 0, "No invented lines in a blank image");
            for (var y = 30; y < 160; y++)
            {
                for (var x = 60; x < 200; x++)
                {
                    gray[y * 256 + x] = .8f;
                }
            }
            var lines = detector.Detect(gray, 256, 192, 100, 30, default);
            Check(lines.Count >= 4, "Rectangle edges detected");
            Check(lines.Any(line => Math.Abs(line.Start[0] - 59.5) < 2 && Math.Abs(line.End[0] - 59.5) < 2), "Vertical edge geometry");
            Check(lines.All(line => line.Descriptor.Length == 128 && line.Descriptor.All(float.IsFinite)), "Finite line descriptors");
            var junctions = detector.Junctions(lines, gray, 256, 192, 100, default);
            Check(junctions.Count >= 4 && junctions.All(point => point.Kind == "junction"), "Rectangle junctions available as point observations");
        });
        tests.Run("Line sensitivity finds faint edges and splitting recovers curved-region segments", () =>
        {
            var faint = new float[256 * 192];
            var curved = new float[512 * 192];
            for (var y = 20; y < 165; y++)
            {
                for (var x = 30; x < 225; x++)
                {
                    faint[y * 256 + x] = .05f;
                }
            }
            for (var y = 20; y < 165; y++)
            {
                for (var x = 30; x < 480; x++)
                {
                    var boundary = 65 + 12 * Math.Sin((x - 30) / 140.0);
                    curved[y * 512 + x] = (float)(Math.Clamp(y - boundary, 0, 1) * .8);
                }
            }
            var original = detector.Detect(faint, 256, 192, 100, 20, default);
            var sensitive = detector.Detect(faint, 256, 192, 100, 20, default,
                new LineDetectionOptions { GradientThreshold = .015, SplitRegions = true });
            Check(sensitive.Count >= 4 && sensitive.Count > original.Count, "Faint rectangle edges survive the configurable threshold");
            var unsplit = detector.Detect(curved, 512, 192, 100, 20, default);
            var split = detector.Detect(curved, 512, 192, 100, 20, default,
                new LineDetectionOptions { SplitRegions = true });
            Check(split.Count > unsplit.Count, $"Straight subsegments recovered: {unsplit.Count} → {split.Count}");
        });
        tests.Run("Line matching preserves segments without fabricating endpoint point matches", () =>
        {
            var a = new FeatureSet { ImageId = "a" };
            var b = new FeatureSet { ImageId = "b" };
            for (var i = 0; i < 3; i++)
            {
                var descriptor = new float[128];
                descriptor[i] = 1;
                a.Lines.Add(new LineFeature { Start = [10, i * 20], End = [100, i * 20], Descriptor = descriptor });
                b.Lines.Add(new LineFeature { Start = [22, i * 20 + 7], End = [112, i * 20 + 7], Descriptor = descriptor.Reverse().ToArray() });
            }
            var result = matcher.Match(a, b, new Settings(), default);
            Check(result.LineMatches.Count == 3, "Mutual line matches including reversed endpoint order");
            Check(result.Matches.Count == 0, "Segment endpoints are not point correspondences");
            Check(result.LineMatches.All(line => line.IndexA == line.IndexB && line.Score == 0), "Saved line identity and descriptor distance");
        });
        tests.Run("Junction point correspondences enter the existing alignment match contract", () =>
        {
            var a = new FeatureSet { ImageId = "a" };
            var b = new FeatureSet { ImageId = "b" };
            for (var i = 0; i < 3; i++)
            {
                var descriptor = new float[64];
                descriptor[i] = 1;
                a.Features.Add(new Feature { X = 40 + i * 20, Y = 80, Kind = "junction", Descriptor = descriptor });
                b.Features.Add(new Feature { X = 30 + i * 20, Y = 80, Kind = "junction", Descriptor = descriptor });
            }
            var result = matcher.Match(a, b, new Settings(), default);
            Check(result.Matches.Count == 3 && result.Matches.All(match => match.Kind == "junction"), "Junction matches preserved");
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
