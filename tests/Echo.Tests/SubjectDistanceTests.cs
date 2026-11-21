using Echo.Contracts;
using Echo.Depth;
using Echo.Projects;

namespace Echo.Tests;

public sealed class SubjectDistanceTests
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;

    public SubjectDistanceTests(IDepthEstimator estimator, IProjectStoreFactory stores)
    {
        this.estimator = estimator;
        this.stores = stores;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Disjoint explicit depth bounds retain the imported photo with an empty depth map", () =>
        {
            var scene = Capture(60, 400);
            var project = Project(scene);
            project.Settings.SubjectDistanceMode = SubjectDistanceMode.Manual;
            project.Settings.SubjectDistanceLimit = 100;
            project.Settings.DepthRangeAutomatic = false;
            project.Settings.DepthNear = 200;
            project.Settings.DepthFar = 800;
            project.Settings.DepthWidth = 400;
            var directory = Path.Combine(Path.GetTempPath(), "echo-background-depth-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init();
            try
            {
                var map = estimator.Estimate(store, project, scene, scene.Cameras[0], default);
                Check(map.ImageId == "0" && map.Width == 400 && map.Height == 300, "Empty maps retain the source camera and requested dimensions");
                Check(map.Depth.All(value => value == 0) && map.Confidence.All(value => value == 0), "Out-of-range terrain cannot become depth evidence");
                Check(project.Images.Count == 12 && scene.Cameras.Count == 12, "No imported photo or aligned camera is removed");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        tests.Run("Distant sparse evidence still permits unmatched foreground to be reconstructed", () =>
        {
            var scene = Capture(60, 400);
            var project = Project(scene);
            var plan = new SubjectDistancePlan { Mode = SubjectDistanceMode.Manual, MaximumDistance = 100 };
            var range = DepthRangeEstimator.Estimate(project, scene, project.Images[0], scene.Cameras[0], default, plan.MaximumDistance);
            var limited = SubjectDistanceFilter.Limit(range, plan);
            Check(limited.SubjectDistanceRejectedObservations > 0 && limited.SampleCount == 0, "The sparse points are all outside the subject limit");
            Check(!limited.OutsideSubjectRange && limited.Near < 100 && limited.Far == 100,
                "Missing foreground matches must not disable a bounded stereo search");
            var uncertain = SubjectDistanceFilter.Limit(new DepthSearchRange(200, 900, 500, 0, "camera bounds fallback"), plan);
            Check(!uncertain.OutsideSubjectRange && uncertain.Near < uncertain.Far,
                "An uncertain camera-bounds fallback cannot veto the subject search");
        });

        tests.Run("Adaptive subject distance keeps the cliff and rejects a heavily matched distant background", () =>
        {
            var scene = Capture(60, 50);
            var project = Project(scene);
            for (var index = 0; index < 20000; index++)
            {
                scene.Points.Add(new CloudPoint
                {
                    ImageA = "0",
                    ImageB = "4",
                    Position = [0, 0, 800],
                    Error = .1
                });
            }

            var plan = SubjectDistancePlanner.Estimate(project, scene, default);
            Check(plan.MaximumDistance is > 60 and < 300, "A few texture-heavy background views cannot dominate the foreground limit");
            Check(plan.EvidenceCameras == 12, "Each supported camera contributes one foreground estimate");
            var range = DepthRangeEstimator.Estimate(project, scene, project.Images[0], scene.Cameras[0], default);
            Check(range.Far > 800, "The uncapped estimator reproduces the distant-terrain trigger");
            var foregroundRange = DepthRangeEstimator.Estimate(project, scene, project.Images[0], scene.Cameras[0], default, plan.MaximumDistance);
            var limited = SubjectDistanceFilter.Limit(foregroundRange, plan);
            Check(limited.Far <= plan.MaximumDistance && limited.Near < 50 && !limited.OutsideSubjectRange,
                "The intended cliff remains searchable while distant terrain is excluded");

            scene.Cameras.Add(new CameraPose { ImageId = "gps-outlier", Centre = [100000, 0, 0] });
            var outlier = SubjectDistancePlanner.Estimate(project, scene, default);
            Check(outlier.MaximumDistance < 300, "One erroneous camera position cannot expand the capture footprint");
        });

        tests.Run("Subject limits scale with wide flights and distant subjects without a fixed metric ceiling", () =>
        {
            var scene = Capture(600, 80);
            var project = Project(scene);
            var wide = SubjectDistancePlanner.Estimate(project, scene, default);
            Check(wide.MaximumDistance > 500, "A wide flight retains a 500 metre scene range");
            foreach (var camera in scene.Cameras)
            {
                camera.Centre = camera.Centre.Select(value => value * 10).ToArray();
            }
            foreach (var point in scene.Points)
            {
                point.Position = point.Position.Select(value => value * 10).ToArray();
            }
            var scaled = SubjectDistancePlanner.Estimate(project, scene, default);
            Check(Math.Abs(scaled.MaximumDistance!.Value - wide.MaximumDistance!.Value * 10) < 1e-8,
                "Automatic limits follow reconstruction scale");

            var compact = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "0" }, new CameraPose { ImageId = "1", Centre = [5, 0, 0] }]
            };
            for (var index = 0; index < 40; index++)
            {
                compact.Points.Add(new CloudPoint { ImageA = "0", ImageB = "1", Position = [2, 0, 150], Error = .1 });
            }
            var distant = SubjectDistancePlanner.Estimate(Project(compact), compact, default);
            Check(distant.MaximumDistance >= 150, "A compact camera footprint must not crop a genuinely distant subject");
        });

        tests.Run("Subject distance controls preserve explicit depth bounds and enforce a custom cap", () =>
        {
            var scene = Capture(60, 50);
            var project = Project(scene);
            project.Settings.DepthRangeAutomatic = false;
            project.Settings.DepthNear = 7;
            project.Settings.DepthFar = 6000;
            Check(SubjectDistancePlanner.Estimate(project, scene, default).MaximumDistance == null,
                "Explicit near/far settings take precedence over an automatic subject heuristic");
            project.Settings.SubjectDistanceMode = SubjectDistanceMode.Manual;
            project.Settings.SubjectDistanceLimit = 90;
            var manual = SubjectDistancePlanner.Estimate(project, scene, default);
            var limited = SubjectDistanceFilter.Limit(new DepthSearchRange(7, 6000, 50, 20, "manual"), manual);
            Check(limited.Near == 7 && limited.Far == 90, "An explicit subject cap intersects the requested depth interval");
            project.Settings.DepthNear = 200;
            var outside = SubjectDistanceFilter.Limit(DepthRangeEstimator.Estimate(project, scene,
                project.Images[0], scene.Cameras[0], default, manual.MaximumDistance), manual);
            Check(outside.OutsideSubjectRange && outside.Near > 0 && outside.Near < outside.Far,
                "Background-only views produce a valid empty-depth decision, not an inverted range");
            var map = new DepthMap { Width = 3, Height = 1, Depth = [50, 90, 91], Confidence = [.8f, .9f, .7f] };
            Check(SubjectDistanceFilter.Apply(map, manual, default) == 1, "The cap also filters samples added by later recovery");
            Check(map.Depth.SequenceEqual(new float[] { 50, 90, 0 }) && map.Confidence[2] == 0,
                "Only out-of-range samples and their confidence are cleared");
            project.Settings.SubjectDistanceMode = SubjectDistanceMode.Off;
            Check(SubjectDistancePlanner.Estimate(project, scene, default).MaximumDistance == null, "Off is unrestricted");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                SubjectDistancePlanner.Estimate(project, scene, cancellation.Token);
                throw new InvalidOperationException("Planning must honour cancellation");
            }
            catch (OperationCanceledException)
            {
            }
        });

        tests.Run("Automatic subject planning handles missing sparse evidence without a fixed default distance", () =>
        {
            var scene = Capture(60, 50);
            scene.Points.Clear();
            var plan = SubjectDistancePlanner.Estimate(Project(scene), scene, default);
            Check(plan.MaximumDistance > 50 && plan.EvidenceCameras == 0, "Camera bounds supply a scale-aware fallback");
            scene.Cameras.ForEach(camera => camera.Centre = [0, 0, 0]);
            Check(SubjectDistancePlanner.Estimate(Project(scene), scene, default).MaximumDistance == null,
                "No usable scale must not invent a distance in metres");
        });
    }

    private ReconstructionScene Capture(double width, double depth)
    {
        var scene = new ReconstructionScene();
        for (var index = 0; index < 12; index++)
        {
            scene.Cameras.Add(new CameraPose { ImageId = index.ToString(), Centre = [-width / 2 + index * width / 11, 0, 0] });
        }
        for (var index = 0; index < 11; index++)
        {
            var x = (scene.Cameras[index].Centre[0] + scene.Cameras[index + 1].Centre[0]) / 2;
            for (var sample = 0; sample < 100; sample++)
            {
                scene.Points.Add(new CloudPoint { ImageA = index.ToString(), ImageB = (index + 1).ToString(), Position = [x, 0, depth], Error = .1 });
            }
        }
        return scene;
    }

    private ProjectData Project(ReconstructionScene scene)
    {
        return new ProjectData
        {
            Images = scene.Cameras.Select(camera => new Photo { Id = camera.ImageId, Width = 800, Height = 600, FocalPixels = 600 }).ToList()
        };
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
