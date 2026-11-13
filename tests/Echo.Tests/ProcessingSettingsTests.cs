using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Matching;
using Echo.Pipeline;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProcessingSettingsTests
{
    private readonly IFeatureMatcher matcher;
    private readonly IImagePairSelector selector;

    [ImportingConstructor]
    public ProcessingSettingsTests(IFeatureMatcher matcher, IImagePairSelector selector)
    {
        this.matcher = matcher;
        this.selector = selector;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Matching thresholds control acceptance and preserve rejection counts", () =>
        {
            var a = new FeatureSet { ImageId = "a", Features = [Feature(0), Feature(2)] };
            var first = Feature(0);
            first.Descriptor[0] = .9f;
            first.Descriptor[1] = (float)Math.Sqrt(.19);
            var b = new FeatureSet { ImageId = "b", Features = [first, Feature(3)] };
            var normal = matcher.Match(a, b, new Settings(), default);
            var strictRatio = matcher.Match(a, b, new Settings { MatchRatioThreshold = .05 }, default);
            var strictDistance = matcher.Match(a, b, new Settings { MatchDistanceThreshold = .1 }, default);
            Check(normal.Matches.Count == 1 && normal.Diagnostics!.Accepted == 1, "Default candidate accepted");
            Check(strictRatio.Matches.Count == 0 && strictRatio.Diagnostics!.RejectedAmbiguous == 1, "Ratio filter");
            Check(strictDistance.Matches.Count == 0 && strictDistance.Diagnostics!.RejectedDistance == 1, "Distance filter");
            foreach (var result in new[] { normal, strictRatio, strictDistance })
            {
                var report = result.Diagnostics!;
                Check(report.Accepted + report.RejectedAmbiguous + report.RejectedDistance + report.RejectedNonMutual == a.Features.Count, "Every first-image feature accounted for");
            }
        });
        tests.Run("All-pairs selection and overlap hints expand the requested comparison set", () =>
        {
            var project = new ProjectData
            {
                Images = Enumerable.Range(0, 4).Select(i => new Photo { Id = i.ToString() }).ToList(),
                Settings = new Settings { PairWindow = 1, GpsPairNeighbours = 0 }
            };
            var units = project.Images.ToDictionary(p => p.Id, p => p.Id);
            Check(selector.Select(project, project.Images, units).Count == 3, "Sequential neighbours");
            project.Hints.Add(new Hint { Type = "overlap", ImageId = "0", OtherImageId = "3" });
            Check(selector.Select(project, project.Images, units).Count == 4, "Hint adds a distant pair");
            project.Settings.MatchAllPairs = true;
            Check(selector.Select(project, project.Images, units).Count == 6, "All unique pairs");
        });
        tests.Run("Work settings reject nonfinite values and impossible support counts", () =>
        {
            foreach (var settings in new[]
            {
                new Settings { MatchRatioThreshold = double.NaN },
                new Settings { MatchDistanceThreshold = double.PositiveInfinity },
                new Settings { CloudNeighbours = 2, CloudMinimumSupport = 3 },
                new Settings { CloudDepthTolerance = 0 },
                new Settings { CloudWorkerThreads = 257 },
                new Settings { CloudBatchRows = 0 },
                new Settings { CloudReprojectionTolerancePixels = double.NaN },
                new Settings { CloudReprojectionTolerancePixels = 0 },
                new Settings { CloudMinimumParallaxDegrees = double.PositiveInfinity },
                new Settings { CloudMinimumParallaxDegrees = -1 },
                new Settings { FusionDetailMultiplier = 17 },
                new Settings { TextureJpegQuality = 101 },
                new Settings { TextureGutter = 0 },
                new Settings { DepthWidth = 8193 },
                new Settings { DepthRefinementSamples = 2 },
                new Settings { DepthNeighbours = 33 }
            })
            {
                var rejected = false;
                try
                {
                    settings.Validate();
                }
                catch (EchoException)
                {
                    rejected = true;
                }

                Check(rejected, "Invalid setting rejected");
            }
        });
    }

    private Feature Feature(int dimension)
    {
        var feature = new Feature { Descriptor = new float[64] };
        feature.Descriptor[dimension] = 1;
        return feature;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
