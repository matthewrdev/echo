using System.Text.Json;
using Echo.Models;
using Echo.Geometry;
using Echo.Reconstruction;

namespace Echo.Tests;

public sealed class VisualReconstructionTests
{
    private readonly IReconstructor reconstructor;

    public VisualReconstructionTests(IReconstructor reconstructor)
    {
        this.reconstructor = reconstructor;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Planar camera registration survives noisy observations and false matches", PlanarRegistration);
        tests.Run("Robust similarity recovers scale and orientation despite GPS outliers", SimilarityReference);
        tests.Run("Collinear camera positions do not invent a geographic orientation", CollinearReference);
        tests.Run("Visual initialization preserves one scene despite inconsistent camera priors", VisualInitialization);
    }

    private void PlanarRegistration()
    {
        var photo = new Photo { Id = "target", Width = 1000, Height = 700, FocalPixels = 850 };
        var truth = new CameraPose
        {
            ImageId = photo.Id,
            Centre = [4, 2, 1],
            Rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = 8, Pitch = -4 })
        };
        var random = new Random(713);
        var points = new List<CloudPoint>();
        var pair = new ImagePair { A = "known", B = photo.Id };
        for (var i = 0; i < 160; i++)
        {
            var point = new V3(random.NextDouble() * 20 - 10, random.NextDouble() * 10 - 5, -25);
            var pixel = CameraGeometry.Project(photo, truth, point)![..2];
            pixel[0] += (random.NextDouble() - .5) * .2;
            pixel[1] += (random.NextDouble() - .5) * .2;
            if (i % 3 == 0)
            {
                pixel = [random.NextDouble() * 1000, random.NextDouble() * 700];
            }
            double[] key = [i, i * 2];
            points.Add(new CloudPoint
            {
                ImageA = "known",
                PixelA = key,
                ImageB = "known2",
                PixelB = key,
                Position = point.Array
            });
            pair.Matches.Add(new Match { A = key, B = pixel });
        }

        var pose = CameraRegistrar.Register(photo, [pair], points, new Random(93));
        Check(pose != null, "Planar camera was not registered");
        Check((new V3(pose!.Centre) - new V3(truth.Centre)).Length < .05, "Planar pose position exceeds 5 cm error");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            CameraRegistrar.Register(photo, [pair], points, new Random(93), cancelled.Token);
            throw new InvalidOperationException("Cancelled registration continued");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SimilarityReference()
    {
        var truth = new SimilarityTransform(7.3,
            CameraGeometry.AttitudeRotation(new Attitude { Yaw = 28, Pitch = -12, Roll = 4 }), new V3(18, -6, 30));
        var random = new Random(31);
        var positions = new List<PositionCorrespondence>();
        for (var i = 0; i < 50; i++)
        {
            var source = new V3(random.NextDouble() * 10, random.NextDouble() * 4, random.NextDouble() * 8);
            var target = truth.Apply(source);
            if (i % 4 == 0)
            {
                target += new V3(100, -200, 300);
            }
            positions.Add(new PositionCorrespondence(source, target, .1));
        }
        var result = SimilarityEstimator.Estimate(positions, new Random(817), default);
        Check(result != null, "Consistent geographic reference was rejected");
        foreach (var position in positions)
        {
            Check((result!.Apply(position.Source) - truth.Apply(position.Source)).Length < 1e-8,
                "GPS outliers distorted the visual reference");
        }
    }

    private void CollinearReference()
    {
        var positions = Enumerable.Range(0, 10).Select(index =>
            new PositionCorrespondence(new V3(index, 0, 0), new V3(0, index * 3, 1), 1)).ToArray();
        Check(SimilarityEstimator.Estimate(positions, new Random(817), default) == null,
            "Collinear positions cannot constrain rotation around the flight line");
    }

    private void VisualInitialization()
    {
        var project = new ProjectData
        {
            Settings = new Settings { BundleIterations = 50, ComputeBackend = ComputeBackend.Cpu }
        };
        V3[] centres = [new(-4, 0, 0), new(-2, 1, .2), new(0, -.5, 0), new(2, .5, -.2), new(4, 2, 1)];
        var truth = new List<CameraPose>();
        for (var i = 0; i < centres.Length; i++)
        {
            var offset = i % 2 == 0 ? 2 : -2;
            var photo = new Photo
            {
                Id = "camera-" + i,
                Width = 1000,
                Height = 700,
                FocalPixels = 850,
                Gps = new GeoPosition
                {
                    Latitude = -33 - centres[i].Z / 111000,
                    Longitude = 150 + (centres[i].X + offset) / 93000,
                    Altitude = 100 + centres[i].Y + offset,
                    UncertaintyMetres = 10
                },
                Attitude = new Attitude { Yaw = i % 2 == 0 ? 166 : 194, Pitch = offset * 3 }
            };
            project.Images.Add(photo);
            truth.Add(new CameraPose { ImageId = photo.Id, Centre = centres[i].Array });
        }

        var random = new Random(132);
        var points = Enumerable.Range(0, 240).Select(_ =>
            new V3(random.NextDouble() * 12 - 6, random.NextDouble() * 8 - 4, 18 + random.NextDouble() * 10)).ToArray();
        var pixels = project.Images.Select((photo, index) => points.Select(point =>
            CameraGeometry.Project(photo, truth[index], point)![..2]).ToArray()).ToArray();
        var pairs = new List<ImagePair>();
        for (var a = 0; a < project.Images.Count; a++)
        {
            for (var b = a + 1; b < project.Images.Count; b++)
            {
                var pair = new ImagePair { A = project.Images[a].Id, B = project.Images[b].Id };
                for (var point = 0; point < points.Length; point++)
                {
                    pair.Matches.Add(new Match { A = pixels[a][point], B = pixels[b][point] });
                }
                pairs.Add(pair);
            }
        }

        var snapshot = JsonSerializer.Serialize(project);
        var updates = new List<OperationProgress>();
        var scene = reconstructor.Align(project, pairs, default, progress: updates.Add);
        Check(updates.All(update => update.Steps != null && update.StepIndex >= 0 && update.StepIndex < update.Steps.Length),
            "Every alignment operation identifies its active substep");
        Check(updates.Any(update => update.Operation == "Register visual cameras" && update.Message.Contains("Registration pass")),
            "Visual registration reports its repeated passes");
        Check(updates.Any(update => update.Operation == "Triangulation" && update.Completed == update.Total && update.Total > 0),
            "Triangulation publishes completed pair counts");
        Check(updates.Any(update => update.Operation == "Solve bundle camera system" && update.Message.Contains("Bundle iteration")),
            "Bundle solving identifies the iteration rather than pretending to be one indefinite operation");
        Check(updates.Last().Operation == "Reference visual reconstruction", "Final visual referencing is visible");
        Check(scene.Cameras.Count == centres.Length && scene.Unplaced.Count == 0, "Visual registration lost photographs");
        Check(scene.Cameras.All(camera => camera.Source.Contains("visual", StringComparison.Ordinal)), "GPS poses bypassed visual registration");
        Check(scene.Origin != null, "Visual reconstruction was not georeferenced");
        Check(scene.Bundle!.FinalMedianError < .05, "Visual observations no longer agree");
        var correspondences = scene.Cameras.Select(camera => new PositionCorrespondence(new V3(camera.Centre),
            centres[project.Images.FindIndex(photo => photo.Id == camera.ImageId)], 10)).ToArray();
        var reference = SimilarityEstimator.Estimate(correspondences, new Random(817), default);
        Check(reference != null, "Could not compare camera geometry to truth");
        Check(correspondences.All(position => (reference!.Apply(position.Source) - position.Target).Length < .05),
            "Noisy camera metadata split the reconstructed camera geometry");
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        var photos = project.Images.ToDictionary(photo => photo.Id);
        foreach (var point in scene.Points)
        {
            var pixel = CameraGeometry.Project(photos[point.ImageA], cameras[point.ImageA], new V3(point.Position));
            Check(pixel != null, "Geographic referencing put a retained point behind its camera");
            var error = Math.Sqrt(Math.Pow(pixel![0] - point.PixelA[0], 2) + Math.Pow(pixel[1] - point.PixelA[1], 2));
            Check(error < .1, "Geographic referencing changed the visual projection");
        }
        Check(JsonSerializer.Serialize(project) == snapshot, "Alignment changed imported metadata or the run snapshot");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
