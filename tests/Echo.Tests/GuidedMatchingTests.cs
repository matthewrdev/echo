using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Matching;

namespace Echo.Tests;

[Export]
public sealed class GuidedMatchingTests
{
    private readonly IGuidedFeatureMatcher guided;

    [ImportingConstructor]
    public GuidedMatchingTests(IGuidedFeatureMatcher guided)
    {
        this.guided = guided;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Robust image geometry recovers limited overlap independently of GPS and focal priors", () =>
        {
            var truth = Matches(60);
            var candidates = new List<Match>(truth);
            var random = new Random(903);
            for (var index = 0; index < 120; index++)
            {
                candidates.Add(new Match { A = [random.Next(1000), random.Next(700)], B = [random.Next(1000), random.Next(700)] });
            }
            // Exact synthetic pixels: use a subpixel tolerance so random outliers
            // passing near a real epipolar line do not count as true correspondences.
            var result = FundamentalMatrixEstimator.Estimate(candidates, .1, default);
            Check(result != null, "Limited overlap must recover a model");
            var correct = 0;
            foreach (var match in result!.Inliers)
            {
                if (truth.Contains(match))
                {
                    correct++;
                }
            }
            Check(correct >= 55 && correct >= result.Inliers.Count * .95, $"Recovered {correct}/{result.Inliers.Count} true matches");
            Check(result.Iterations > 128, "Low overlap must not truncate the search after a tiny early consensus");
            var degenerate = new List<Match>();
            for (var index = 0; index < 40; index++)
            {
                degenerate.Add(new Match { A = [index, index], B = [index * 2, index * 2] });
            }
            Check(FundamentalMatrixEstimator.Estimate(degenerate, 1, default) == null, "Collinear correspondences cannot determine epipolar geometry");
        });
        tests.Run("Guided second pass recovers known correspondences and rejects descriptor aliases off the epipolar line", () =>
        {
            var truth = Matches(80);
            var left = new FeatureSet { ImageId = "left" };
            var right = new FeatureSet { ImageId = "right" };
            var random = new Random(314);
            foreach (var match in truth)
            {
                var descriptor = new float[32];
                double norm = 0;
                for (var index = 0; index < descriptor.Length; index++)
                {
                    descriptor[index] = (float)(random.NextDouble() * 2 - 1);
                    norm += descriptor[index] * descriptor[index];
                }
                for (var index = 0; index < descriptor.Length; index++)
                {
                    descriptor[index] /= (float)Math.Sqrt(norm);
                }
                left.Features.Add(new Feature { X = match.A[0], Y = match.A[1], Descriptor = descriptor });
                right.Features.Add(new Feature { X = match.B[0], Y = match.B[1], Descriptor = descriptor });
                right.Features.Add(new Feature { X = match.B[0] + 35, Y = match.B[1] + 190, Descriptor = descriptor });
            }
            var seed = truth.GetRange(0, 25);
            var recovered = guided.Recover(left, right, seed, new Settings(), 1, default);
            Check(recovered != null && recovered.Matches.Count >= 70, $"Second pass should recover at least 70 of 80 real observations; got {recovered?.Matches.Count ?? 0}");
            foreach (var match in recovered!.Matches)
            {
                Check(truth.Exists(candidate => candidate.A[0] == match.A[0] && candidate.B[0] == match.B[0]
                    && candidate.B[1] == match.B[1]), "Guidance must not admit descriptor aliases");
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                guided.Recover(left, right, seed, new Settings(), 1, cancelled.Token);
                throw new InvalidOperationException("Cancellation must abort recovery");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private List<Match> Matches(int count)
    {
        var photo = new Photo { Width = 1000, Height = 700, FocalPixels = 850 };
        var left = new CameraPose();
        var right = new CameraPose { Centre = [1, .2, .05] };
        var random = new Random(623);
        var result = new List<Match>(count);
        for (var index = 0; index < count; index++)
        {
            var point = new V3(random.NextDouble() * 5 - 2, random.NextDouble() * 3 - 1, 6 + random.NextDouble() * 8);
            result.Add(new Match { A = CameraGeometry.Project(photo, left, point)![..2], B = CameraGeometry.Project(photo, right, point)![..2] });
        }
        return result;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
