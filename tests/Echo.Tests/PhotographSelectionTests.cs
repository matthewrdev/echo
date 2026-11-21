using Echo.Contracts;
using Echo.Geometry;
using Echo.Meshing;
using Echo.Storage;

namespace Echo.Tests;

public sealed class PhotographSelectionTests
{
    private readonly IMeshBuilder meshes;

    public PhotographSelectionTests(IMeshBuilder meshes)
    {
        this.meshes = meshes;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("High-camera visibility batches preserve measured and gap selection under a small memory budget", () =>
        {
            var photos = Enumerable.Range(0, 439).Select(index => new Photo
            {
                Id = index.ToString(),
                Width = 100,
                Height = 100,
                FocalPixels = 100
            }).ToArray();
            var project = new ProjectData { Images = photos.ToList() };
            var scene = new ReconstructionScene
            {
                Cameras = photos.Select((photo, index) => new CameraPose
                {
                    ImageId = photo.Id,
                    Centre = [index < 4 ? 0 : 100, 0, 0]
                }).ToList()
            };
            var maps = photos.Select(photo => new DepthMap
            {
                ImageId = photo.Id,
                Width = 100,
                Height = 100,
                Depth = Enumerable.Range(0, 10000).Select(index => index % 100 < 50 ? 10f : 0f).ToArray()
            }).ToArray();
            var mesh = new Surface();
            for (var face = 0; face < 513; face++)
            {
                var x = face % 27 * .12 - 1.5;
                var y = face / 27 * .12 - 1;
                var first = mesh.Vertices.Count;
                mesh.Vertices.Add([x, y, 10]);
                mesh.Vertices.Add([x + .1, y, 10]);
                mesh.Vertices.Add([x, y + .1, 10]);
                mesh.Faces.Add([first, first + 1, first + 2]);
            }
            var views = MeshDepthViews.Build(project, scene, maps);
            var byId = photos.ToDictionary(photo => photo.Id);
            var expected = FaceViewSelection.Select(mesh, views, byId, default, optimizeSeams: false);
            Check(expected.All(view => view == 0), "Measured and inferred faces must retain the first equally detailed visible photograph");
            var directory = Path.Combine(Path.GetTempPath(), "echo-visibility-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var data = new BulkDataSession(65536, 0);
                using var work = new WorkBuffers(data, directory);
                var actual = FaceViewSelection.Select(mesh, views, byId, default, work: work, optimizeSeams: false);
                Check(actual.SequenceEqual(expected), "Memory-dependent visibility batches changed photograph assignments");
                Check(data.Statistics.PeakResidentBytes <= 65536, "Visibility reservations exceeded the shared page budget");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        });
        tests.Run("Completion verifies photographs without running seam optimization", () =>
        {
            var project = new ProjectData { Images = [new Photo { Id = "photo", Width = 100, Height = 100, FocalPixels = 50 }] };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "photo" }] };
            var operations = new List<string>();
            var observed = meshes.AssignCameras(project, scene, [Map("photo", 100, 10)], Triangle(), default,
                operationProgress: operation => operations.Add(operation.Operation), forCompletion: true);
            Check(observed.SourceImages.SequenceEqual(["photo"]) && observed.Vertices.Count == 3, "Completion lost verified geometry");
            Check(!operations.Contains("Choose coherent photographs") && !operations.Contains("Build seam neighbours"), "Completion still paid for texture seam optimization");
        });
        tests.Run("Photograph candidate pruning applies density before the top-32 score limit", () =>
        {
            var options = Enumerable.Range(0, 64).Select(view => new FaceViewCandidate(view, 1000 - view, 10)).ToList();
            options.Add(new FaceViewCandidate(64, 1, 100));
            FaceViewSelection.RetainDetail(options);
            Check(options.Count == 1 && options[0].View == 64, "Early score pruning discarded the detailed photograph");
            options = Enumerable.Range(0, 64).Reverse().Select(view => new FaceViewCandidate(view, 1, 100)).ToList();
            FaceViewSelection.RetainDetail(options);
            Check(options.Select(option => option.View).SequenceEqual(Enumerable.Range(0, 32)), "Tied candidates lost camera ordering");
        });
        tests.Run("Occluded detailed views cannot prune a visible coarse photograph across a gap", () =>
        {
            var detailed = new Photo { Id = "detailed", Width = 400, Height = 400, FocalPixels = 200 };
            var coarse = new Photo { Id = "coarse", Width = 100, Height = 100, FocalPixels = 50 };
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "detailed" }, new CameraPose { ImageId = "coarse", Centre = [-5, 0, 0] }]
            };
            var surface = Triangle();
            surface.Vertices = [.. surface.Vertices, [-.2, -.4, 5], [.2, -.4, 5], [0, 0, 5]];
            surface.Faces.Add([3, 4, 5]);
            foreach (var measured in new[] { false, true })
            {
                foreach (var reverse in new[] { false, true })
                {
                    var project = new ProjectData { Images = reverse ? [coarse, detailed] : [detailed, coarse] };
                    var maps = project.Images.Select(photo => Map(photo.Id, 100, measured ? 10 : 0)).ToArray();
                    var views = MeshDepthViews.Build(project, scene, maps);
                    var selected = FaceViewSelection.Select(surface, views, project.Images.ToDictionary(photo => photo.Id), default);
                    Check(selected[0] >= 0 && views[selected[0]].Camera.ImageId == "coarse",
                        "An occluded high-detail view removed the visible source, or view ordering changed the choice");
                }
            }
        });
        tests.Run("Photo selection reports gap counts and all phases without duplicate text spam", () =>
        {
            var project = new ProjectData { Images = [new Photo { Id = "photo", Width = 100, Height = 100, FocalPixels = 50 }] };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "photo" }] };
            var updates = new List<OperationProgress>();
            var text = new List<string>();
            var result = meshes.AssignCameras(project, scene, [Map("photo", 100, 0)], Triangle(), default,
                progress: text.Add, operationProgress: updates.Add);
            Check(result.SourceImages.Single() == "photo", "Missing depth should use verified geometric visibility");
            Check(updates.Select(update => update.StepIndex).Distinct().SequenceEqual(Enumerable.Range(0, 6).Select(index => (int?)index)),
                "Photograph selection must expose index, measured, gap, neighbours, coherence and assembly phases");
            var gaps = updates.Where(update => update.Operation == "Check completed gaps").ToArray();
            Check(gaps.First().Completed == 0 && gaps.Last().Completed == 1 && gaps.Last().Total == 1,
                "Completed-gap visibility must report its actual face count");
            Check(updates.Last().Completed == 1 && updates.Last().Total == 1 && text.Count == 0,
                "Counted progress was duplicated as text or assembly completion was lost");
        });
        tests.Run("Concurrent photograph progress is monotonic and rate limited", () =>
        {
            var updates = new List<OperationProgress>();
            var reporter = new PhotographSelectionProgress(null, updates.Add);
            reporter.Start(PhotographSelectionPhase.CheckMeasuredVisibility, 100000);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(0, 100000, _ => reporter.Increment());
            reporter.Complete();
            Check(updates.Count <= 2 + Math.Ceiling(clock.Elapsed.TotalSeconds * 8), "Photo selection emitted per-face progress spam");
            Check(updates.Select(update => update.Completed).SequenceEqual(updates.Select(update => update.Completed).Order()),
                "Parallel photo progress moved backwards");
            Check(updates.Last().Completed == 100000, "Parallel progress lost completed faces");
        });
        tests.Run("Photograph detail is independent of supporting depth-map resolution", () =>
        {
            var project = new ProjectData
            {
                Images =
                [
                    new Photo { Id = "small", Width = 100, Height = 100, FocalPixels = 100 },
                    new Photo { Id = "large", Width = 400, Height = 400, FocalPixels = 400 }
                ]
            };
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "small" }, new CameraPose { ImageId = "large" }]
            };
            var maps = new[] { Map("small", 100, 10), Map("large", 100, 10) };
            var mesh = Triangle();
            var selected = meshes.AssignCameras(project, scene, maps, mesh, default);
            Check(selected.SourceImages.Single() == "large", "Same depth resolution must not hide a four-times finer photo sampling grid");

            maps[1] = Map("large", 50, 10);
            selected = meshes.AssignCameras(project, scene, maps, mesh, default);
            Check(selected.SourceImages.Single() == "large", "Reducing depth-map size cannot reduce the texture-photo score");
        });

        tests.Run("Close photographs win when they resolve more of a visible surface", () =>
        {
            var project = new ProjectData
            {
                Images = [new Photo { Id = "far", Width = 100, Height = 100, FocalPixels = 100 },
                    new Photo { Id = "close", Width = 100, Height = 100, FocalPixels = 100 }]
            };
            var scene = new ReconstructionScene
            {
                Cameras = [new CameraPose { ImageId = "far" }, new CameraPose { ImageId = "close", Centre = [0, 0, 5] }]
            };
            var maps = new[] { Map("far", 100, 10), Map("close", 100, 5) };
            var selected = meshes.AssignCameras(project, scene, maps, Triangle(), default);
            Check(selected.SourceImages.Single() == "close", "The closer photograph supplies four times as many source pixels");

            Array.Fill(maps[1].Depth, 2f);
            selected = meshes.AssignCameras(project, scene, maps, Triangle(), default);
            Check(selected.SourceImages.Single() == "far", "Higher resolution cannot override measured foreground occlusion");
        });

        tests.Run("Seam smoothing cannot replace a detailed photograph with a coarse neighbour", () =>
        {
            var candidates = new List<FaceViewCandidate>
            {
                new(0, 100, 100), new(1, 90, 81), new(2, 80, 80), new(3, 60, 25)
            };
            FaceViewSelection.RetainDetail(candidates);
            Check(candidates.Select(candidate => candidate.View).SequenceEqual([0, 1]),
                "Retain at least 90% linear resolution before graph-cut seam optimisation");
        });

        tests.Run("Removing floating islands before photo assignment releases valid source views", () =>
        {
            var project = new ProjectData { Images = [new Photo { Id = "photo", Width = 100, Height = 100, FocalPixels = 50 }] };
            var scene = new ReconstructionScene { Cameras = [new CameraPose { ImageId = "photo" }] };
            var map = Map("photo", 100, 10);
            var mesh = Triangle();
            mesh.Vertices = [.. mesh.Vertices, [-.2, -.4, 5], [.2, -.4, 5], [0, 0, 5]];
            mesh.Faces.Add([3, 4, 5]);
            var views = MeshDepthViews.Build(project, scene, [map]);
            var before = FaceViewSelection.Select(mesh, views, project.Images.ToDictionary(photo => photo.Id), default);
            Check(before[0] < 0, "The floating foreground triangle really blocks the source view");
            var trimmed = SurfaceIslandTrimmer.Trim(mesh, IslandTrimMode.Main, .1, default).Surface;
            var after = meshes.AssignCameras(project, scene, [map], trimmed, default, retainUnobserved: true);
            Check(after.Faces.Count == 1 && after.SourceImages.Single() == "photo", "Assignment must use the final surviving geometry");
        });

        tests.Run("Photographic coverage reports surface area separately from triangle counts", () =>
        {
            var mesh = Triangle();
            mesh.Vertices = [.. mesh.Vertices, [-2, -2, 20], [2, -2, 20], [0, 2, 20]];
            mesh.Faces.Add([3, 4, 5]);
            mesh.SourceImages = ["", "photo"];
            var coverage = SurfacePhotographCoverage.Measure(mesh, default);
            Check(coverage.InferredFaces == 1 && coverage.Faces == 2, "Half the triangles have inferred colour");
            Check(Math.Abs(coverage.InferredAreaPercent - 20) < 1e-10, "Only one fifth of the surface area has inferred colour");
        });
    }

    private Surface Triangle() => new()
    {
        Vertices = [[-1, -1, 10], [1, -1, 10], [0, 1, 10]],
        Faces = [[0, 1, 2]]
    };

    private DepthMap Map(string id, int size, float depth) => new()
    {
        ImageId = id,
        Width = size,
        Height = size,
        Depth = Enumerable.Repeat(depth, size * size).ToArray()
    };

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
