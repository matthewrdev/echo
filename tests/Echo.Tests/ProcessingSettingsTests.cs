using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
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
                new Settings { OptimiseVegetation = true, VegetationTextureScale = double.NaN },
                new Settings { VegetationTextureScale = 0 },
                new Settings { VegetationMeshToleranceMultiplier = 5 },
                new Settings { LineGradientThreshold = double.NaN },
                new Settings { LineGradientThreshold = 0 },
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
                new Settings { FusionIslandMode = (IslandTrimMode)999 },
                new Settings { FusionIslandMinimumSizePercent = double.NaN },
                new Settings { FusionIslandMinimumSizePercent = -1 },
                new Settings { FusionIslandMinimumSizePercent = 101 },
                new Settings { TextureSkyMarginPixels = 0 },
                new Settings { TextureSkyMarginPixels = 65 },
                new Settings { MeshIslandMode = (IslandTrimMode)999 },
                new Settings { MeshIslandMinimumAreaPercent = double.NaN },
                new Settings { MeshIslandMinimumAreaPercent = double.PositiveInfinity },
                new Settings { MeshIslandMinimumAreaPercent = -1 },
                new Settings { MeshIslandMinimumAreaPercent = 101 },
                new Settings { TextureJpegQuality = 101 },
                new Settings { TextureGutter = 0 },
                new Settings { DepthWidth = 8193 },
                new Settings { DepthRefinementSamples = 2 },
                new Settings { DepthNeighbours = 33 },
                new Settings { FrustumPairNeighbours = -1 },
                new Settings { PairSearchDistance = double.NaN }
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
        tests.Run("Frustum overlap includes crossing views and rejects separated volumes", () =>
        {
            double[] forward = [1, 0, 0, 0, 1, 0, 0, 0, 1];
            double[] backward = [-1, 0, 0, 0, 1, 0, 0, 0, -1];
            var lower = new CaptureFrustum(new V3(0, 0, 0), forward, .5, .5, 50, 0);
            var upper = new CaptureFrustum(new V3(0, 10, 0), forward, .5, .5, 50, 0);
            var distant = new CaptureFrustum(new V3(0, 100, 0), forward, .5, .5, 50, 0);
            var away = new CaptureFrustum(new V3(0, 0, -10), backward, .5, .5, 50, 0);
            Check(FrustumOverlap.Score(lower, upper) > 0, "Different heights share viewing volume");
            Check(FrustumOverlap.Score(lower, distant) == 0, "Disjoint elevation ranges");
            Check(FrustumOverlap.Score(lower, away) == 0, "Nearby cameras looking away do not overlap");
            Check(Math.Abs(FrustumOverlap.Score(lower, upper) - FrustumOverlap.Score(upper, lower)) < 1e-12, "Symmetric ranking");
        });
        tests.Run("Spatial selection reaches other flight passes and uses altitude for GPS distance", () =>
        {
            var project = new ProjectData
            {
                Settings = new Settings { PairWindow = 1, GpsPairNeighbours = 0, FrustumPairNeighbours = 2, PairSearchDistance = 100 },
                Images = [Capture("a", 0, 10), Capture("b", 1, 10), Capture("c", 2, 10),
                    Capture("d", 3, 10), Capture("e", 0, 25), Capture("f", 0, 200), Capture("g", 1, 25)]
            };
            var units = project.Images.ToDictionary(photo => photo.Id, photo => photo.Id);
            var pairs = selector.Select(project, project.Images, units);
            Check(pairs.Any(pair => pair.A == "a" && pair.B == "e"), "Non-sequential upper pass included");
            Check(!pairs.Any(pair => pair.A == "a" && pair.B == "f"), "Non-overlapping high capture excluded");
            project.Settings.FrustumPairNeighbours = 0;
            project.Settings.GpsPairNeighbours = 1;
            pairs = selector.Select(project, project.Images, units);
            Check(!pairs.Any(pair => pair.A == "a" && pair.B == "e"), "Altitude separates vertically stacked GPS positions");
            project.Settings.GpsPairNeighbours = 0;
            project.Settings.FrustumPairNeighbours = 2;
            project.Hints.Add(new Hint { Type = "camera", ImageId = "f", Position = project.Images[4].Gps });
            pairs = selector.Select(project, project.Images, units);
            Check(pairs.Any(pair => pair.A == "a" && pair.B == "f"), "Camera position hints guide pair selection");
        });
    }

    private Photo Capture(string imageId, double eastMetres, double altitude)
    {
        return new Photo
        {
            Id = imageId,
            Width = 1000,
            Height = 800,
            FocalPixels = 1000,
            Gps = new GeoPosition { Latitude = 0, Longitude = eastMetres / 111320, Altitude = altitude, UncertaintyMetres = 1 },
            Attitude = new Attitude()
        };
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
