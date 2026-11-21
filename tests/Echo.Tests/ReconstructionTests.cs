using System.Globalization;
using System.Text.Json;
using Echo.Geometry;
using Echo.Models;
using Echo.Pipeline.Artifacts;
using SkiaSharp;

namespace Echo.Tests;

public sealed class ReconstructionTests
{
    private readonly Echo.Imaging.IRasterImageService rasterImageService;
    private readonly Echo.Infrastructure.IJsonCodec jsonCodec;
    private readonly Echo.Infrastructure.IFileOperations fileOperations;
    private readonly Echo.Reconstruction.IReconstructor reconstructor;
    private readonly Echo.Optimization.IBundleAdjuster bundleAdjuster;
    private readonly Echo.Meshing.IMeshBuilder meshBuilder;
    private readonly Echo.Simplification.IEdgeCollapse edgeCollapse;
    private readonly Echo.Export.IGlbWriter glbWriter;
    private readonly Echo.Projects.IProjectStoreFactory storeFactory;
    private readonly Echo.Pipeline.IProcessor processor;
    private readonly ProcessingSettingsTests settingsTests;
    private readonly TextureTests textureTests;
    private readonly ProcessingEfficiencyTests efficiencyTests;
    private readonly LineFeatureTests lineTests;
    private readonly ResetCommandTests resetCommandTests;
    private readonly FeatureExtractionTests featureExtractionTests;
    private readonly CloudProcessingTests cloudProcessingTests;
    private readonly VisualReconstructionTests visualReconstructionTests;
    private readonly Echo.Pipeline.Artifacts.IStageResetService resetService;
    public ReconstructionTests(Echo.Imaging.IRasterImageService rasterImageService, Echo.Infrastructure.IJsonCodec jsonCodec, Echo.Infrastructure.IFileOperations fileOperations, Echo.Reconstruction.IReconstructor reconstructor, Echo.Optimization.IBundleAdjuster bundleAdjuster, Echo.Meshing.IMeshBuilder meshBuilder, Echo.Simplification.IEdgeCollapse edgeCollapse, Echo.Export.IGlbWriter glbWriter, Echo.Projects.IProjectStoreFactory storeFactory, Echo.Pipeline.IProcessor processor, ProcessingSettingsTests settingsTests, TextureTests textureTests, Echo.Pipeline.Artifacts.IStageResetService resetService, ProcessingEfficiencyTests efficiencyTests, LineFeatureTests lineTests, ResetCommandTests resetCommandTests, FeatureExtractionTests featureExtractionTests, CloudProcessingTests cloudProcessingTests, VisualReconstructionTests visualReconstructionTests)
    {
        this.rasterImageService = rasterImageService;
        this.jsonCodec = jsonCodec;
        this.fileOperations = fileOperations;
        this.reconstructor = reconstructor;
        this.bundleAdjuster = bundleAdjuster;
        this.meshBuilder = meshBuilder;
        this.edgeCollapse = edgeCollapse;
        this.glbWriter = glbWriter;
        this.storeFactory = storeFactory;
        this.processor = processor;
        this.settingsTests = settingsTests;
        this.textureTests = textureTests;
        this.efficiencyTests = efficiencyTests;
        this.lineTests = lineTests;
        this.resetCommandTests = resetCommandTests;
        this.featureExtractionTests = featureExtractionTests;
        this.cloudProcessingTests = cloudProcessingTests;
        this.visualReconstructionTests = visualReconstructionTests;
        this.resetService = resetService;
    }

    public int Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var tests = new TestSuite();
        settingsTests.Run(tests);
        textureTests.Run(tests);
        efficiencyTests.Run(tests);
        lineTests.Run(tests);
        resetCommandTests.Run(tests);
        featureExtractionTests.Run(tests);
        cloudProcessingTests.Run(tests);
        visualReconstructionTests.Run(tests);
        tests.Run("Composition registers all processing stages in dependency order", () =>
        {
            var expected = new[]
            {
                "features",
                "matching",
                "alignment",
                "depth",
                "cloud",
                "fusion",
                "mesh",
                "texture",
                "export"
            };
            Check(processor.Stages.SequenceEqual(expected), "Incomplete or incorrectly ordered stage registration");
        });
        tests.Run("WGS84 axes and DJI camera convention", () =>
        {
            var origin = new GeoPosition
            {
                Latitude = -33,
                Longitude = 150,
                Altitude = 100
            };
            var east = CameraGeometry.ToLocal(new GeoPosition { Latitude = -33, Longitude = 150.00001, Altitude = 100 }, origin);
            var north = CameraGeometry.ToLocal(new GeoPosition { Latitude = -32.99999, Longitude = 150, Altitude = 100 }, origin);
            var up = CameraGeometry.ToLocal(new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 101 }, origin);
            Check(east.X > .8 && Math.Abs(east.Z) < .01, "East axis");
            Check(north.Z < -1 && Math.Abs(north.X) < .01, "North is -Z");
            Near(up.Y, 1, 1e-6);
            var matrix = CameraGeometry.LocalToEcefMatrix(origin);
            var ecef = CameraGeometry.Ecef(origin);
            Near(matrix[3], ecef.X, 1e-8);
            Near(matrix[7], ecef.Y, 1e-8);
            Near(matrix[11], ecef.Z, 1e-8);
            var testGeo = new GeoPosition
            {
                Latitude = -32.99999,
                Longitude = 150.00001,
                Altitude = 104
            };
            var local = CameraGeometry.ToLocal(testGeo, origin);
            var expected = CameraGeometry.Ecef(testGeo);
            Near(matrix[0] * local.X + matrix[1] * local.Y + matrix[2] * local.Z + matrix[3], expected.X, 1e-6);
            Near(matrix[4] * local.X + matrix[5] * local.Y + matrix[6] * local.Z + matrix[7], expected.Y, 1e-6);
            Near(matrix[8] * local.X + matrix[9] * local.Y + matrix[10] * local.Z + matrix[11], expected.Z, 1e-6);
            var rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = 90, Pitch = -30 });
            var right = CameraGeometry.Rotate(rotation, new V3(1, 0, 0));
            var down = CameraGeometry.Rotate(rotation, new V3(0, 1, 0));
            var forward = CameraGeometry.Rotate(rotation, new V3(0, 0, 1));
            Near(forward.X, Math.Sqrt(.75), 1e-9);
            Near(forward.Y, -.5, 1e-9);
            Near(((right).Cross(down)).Dot(forward), 1, 1e-9);
        });
        tests.Run("Triangulation recovers metric geometry and rejects rays behind cameras", () =>
        {
            var image = new Photo
            {
                Width = 1000,
                Height = 700,
                FocalPixels = 900
            };
            var a = new CameraPose();
            var b = new CameraPose
            {
                Centre = [2, 0, 0]
            };
            var point = new V3(.7, .2, 11);
            var match = new Match
            {
                A = CameraGeometry.Project(image, a, point)![..2],
                B = CameraGeometry.Project(image, b, point)![..2]
            };
            var recovered = CameraGeometry.Triangulate(image, image, a, b, match);
            Check(recovered != null && (recovered.Point - point).Length < 1e-8, "Metric triangulation");
            Check(CameraGeometry.Triangulate(image, image, a, b, new Match { A = [400, 300], B = [600, 300] }) == null, "Behind camera rejection");
        });
        tests.Run("Eight-point RANSAC handles a non-GPS image pair with outliers", () =>
        {
            var image = new Photo
            {
                Width = 1000,
                Height = 700,
                FocalPixels = 850
            };
            var a = new CameraPose();
            var b = new CameraPose
            {
                Centre = [1, .1, .05]
            };
            var random = new Random(23);
            var matches = new List<Match>();
            for (var i = 0; i < 60; i++)
            {
                var point = new V3(random.NextDouble() * 5 - 2, random.NextDouble() * 3 - 1, 6 + random.NextDouble() * 8);
                matches.Add(new Match { A = CameraGeometry.Project(image, a, point)![..2], B = CameraGeometry.Project(image, b, point)![..2] });
            }

            for (var i = 0; i < 12; i++)
            {
                matches.Add(new Match { A = [random.Next(1000), random.Next(700)], B = [random.Next(1000), random.Next(700)] });
            }

            var result = CameraGeometry.EstimateRelative(image, image, matches);
            Check(result != null && result.Inliers.Count >= 55, "Visual relative pose");
            var centre = CameraGeometry.Rotate(CameraGeometry.Transpose(result!.Rotation), new V3(result.Translation)) * -1;
            Check((centre.Unit).Dot(new V3(b.Centre).Unit) > .99, "Translation direction and cheirality");
        });
        tests.Run("Ground camera registers against GPS cameras with different intrinsics", () =>
        {
            var origin = new GeoPosition
            {
                Latitude = -33,
                Longitude = 150,
                Altitude = 100
            };
            var gpsB = new GeoPosition
            {
                Latitude = -33,
                Longitude = 150.00002,
                Altitude = 100
            };
            var a = new Photo
            {
                Id = "a",
                Width = 1000,
                Height = 700,
                FocalPixels = 850,
                Gps = origin,
                Attitude = new Attitude()
            };
            var b = new Photo
            {
                Id = "b",
                Width = 1000,
                Height = 700,
                FocalPixels = 850,
                Gps = gpsB,
                Attitude = new Attitude()
            };
            var ground = new Photo
            {
                Id = "ground",
                Width = 1200,
                Height = 900,
                FocalPixels = 1000
            };
            var ca = new CameraPose
            {
                ImageId = "a",
                Rotation = CameraGeometry.AttitudeRotation(new Attitude())
            };
            var cb = new CameraPose
            {
                ImageId = "b",
                Centre = CameraGeometry.ToLocal(gpsB, origin).Array,
                Rotation = ca.Rotation
            };
            var cg = new CameraPose
            {
                ImageId = "ground",
                Centre = [1, .4, -.2],
                Rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = 5, Pitch = -3 })
            };
            var ab = new ImagePair
            {
                A = "a",
                B = "b"
            };
            var bg = new ImagePair
            {
                A = "b",
                B = "ground"
            };
            var random = new Random(812);
            for (var i = 0; i < 70; i++)
            {
                var point = new V3(random.NextDouble() * 4 - 2, random.NextDouble() * 3 - 1, -8 - random.NextDouble() * 8);
                ab.Matches.Add(new Match { A = CameraGeometry.Project(a, ca, point)![..2], B = CameraGeometry.Project(b, cb, point)![..2] });
                bg.Matches.Add(new Match { A = CameraGeometry.Project(b, cb, point)![..2], B = CameraGeometry.Project(ground, cg, point)![..2] });
            }

            var project = new ProjectData { Images = [a, b, ground] };
            var original = fileOperations.Hash(project);
            var result = reconstructor.Align(project, [ab, bg], default);
            Check(fileOperations.Hash(project) == original, "Alignment does not change input photos or priors");
            Check(result.Origin != null && !ReferenceEquals(result.Origin, origin), "The output owns its geographic origin");
            var solved = result.Cameras.Single(c => c.ImageId == "ground");
            Check((new V3(solved.Centre) - new V3(cg.Centre)).Length < .001 && result.Unplaced.Count == 0, "Ground camera position");
            Check(solved.Source.StartsWith("visual DLT registration", StringComparison.Ordinal) && ground.Gps == null, "Registered from visual observations rather than GPS");
        });
        tests.Run("Joint bundle adjustment corrects noisy camera positions and orientations", () =>
        {
            var project = new ProjectData
            {
                Settings = new Settings
                {
                    BundleIterations = 35
                }
            };
            var scene = new ReconstructionScene
            {
                Origin = new GeoPosition
                {
                    Latitude = -33,
                    Longitude = 150,
                    Altitude = 100
                }
            };
            var truth = new List<CameraPose>();
            for (var i = 0; i < 5; i++)
            {
                var image = new Photo
                {
                    Id = "bundle-" + i,
                    Camera = "synthetic",
                    Width = 1000,
                    Height = 700,
                    FocalPixels = 850,
                    Gps = scene.Origin,
                    Attitude = new Attitude
                    {
                        Yaw = i * 3
                    }
                };
                project.Images.Add(image);
                var pose = new CameraPose
                {
                    ImageId = image.Id,
                    Centre = [i * 1.2, i * .12, 0],
                    Rotation = CameraGeometry.AttitudeRotation(image.Attitude),
                    FocalPixels = 850
                };
                truth.Add(pose);
                scene.Cameras.Add(new CameraPose { ImageId = image.Id, Centre = i == 0 ? pose.Centre : [i * 1.2 + .25 * Math.Sin(i), i * .12 + .2 * Math.Cos(i), .15 * Math.Sin(i)], Rotation = CameraGeometry.AttitudeRotation(new Attitude { Yaw = i * 3 + (i == 0 ? 0 : 1.5 * Math.Cos(i)), Pitch = i == 0 ? 0 : .9 * Math.Sin(i) }) });
            }

            var random = new Random(125);
            var points = Enumerable.Range(0, 90).Select(_ => new V3(random.NextDouble() * 7 - 1, random.NextDouble() * 4 - 2, -10 - random.NextDouble() * 10)).ToList();
            var pairs = new List<ImagePair>();
            for (var a = 0; a < 5; a++)
            {
                for (var b = a + 1; b < 5; b++)
                {
                    var pair = new ImagePair
                    {
                        A = project.Images[a].Id,
                        B = project.Images[b].Id
                    };
                    foreach (var point in points)
                    {
                        pair.Matches.Add(new Match { A = CameraGeometry.Project(project.Images[a], truth[a], point)![..2], B = CameraGeometry.Project(project.Images[b], truth[b], point)![..2] });
                    }

                    pairs.Add(pair);
                }
            }

            bundleAdjuster.Refine(project, scene, pairs, default);
            Console.WriteLine($"  Bundle median error: {scene.Bundle!.InitialMedianError:F3} → {scene.Bundle.FinalMedianError:F3} pixels; {scene.Bundle.AcceptedIterations} accepted steps");
            Check(scene.Bundle.AcceptedIterations >= 3 && scene.Bundle.FinalMedianError < .2 && scene.Bundle.FinalMedianError < scene.Bundle.InitialMedianError * .1, "Joint visual error reduction");
        });
        var temp = Path.Combine(Path.GetTempPath(), "echo-tests-" + Guid.NewGuid().ToString("N"));
        tests.Run("Fused surface and edge-collapse budget preserve a continuous plane", () =>
        {
            var volume = new FusedVolume
            {
                VoxelSize = 1
            };
            for (var x = 0; x <= 30; x++)
            {
                for (var y = 0; y <= 20; y++)
                {
                    for (var z = -2; z <= 2; z++)
                    {
                        volume.Samples.Add(new VolumeSample { X = x, Y = y, Z = z, Distance = z - .25f, Views = 3 });
                    }
                }
            }

            var mesh = meshBuilder.Extract(volume, default);
            var reduced = edgeCollapse.Reduce(mesh, 200, default);
            foreach (var surface in new[]
            {
                mesh,
                reduced
            }

            )
            {
                double area = 0;
                var edges = new HashSet<string>();
                var used = new HashSet<int>();
                foreach (var f in surface.Faces)
                {
                    area += (new V3(surface.Vertices[f[1]]) - new V3(surface.Vertices[f[0]])).Cross(new V3(surface.Vertices[f[2]]) - new V3(surface.Vertices[f[0]])).Length * .5;
                    for (var i = 0; i < 3; i++)
                    {
                        var a = f[i];
                        var b = f[(i + 1) % 3];
                        edges.Add($"{Math.Min(a, b)}:{Math.Max(a, b)}");
                        used.Add(a);
                    }
                }

                Near(area, 30 * 20, .001);
                Check(used.Count - edges.Count + surface.Faces.Count == 1, "A disk remains a disk without new holes");
                Check(surface.Vertices.All(p => Math.Abs(p[2] - .25) < 1e-6), "Extracted surface depth");
            }

            Check(reduced.Faces.Count <= 200, "Mesh target reached by edge collapse");
        });
        Directory.CreateDirectory(temp);
        try
        {
            var source = Path.Combine(temp, "photos");
            Directory.CreateDirectory(source);
            MakePhotos(source);
            var store = storeFactory.Open(Path.Combine(temp, "project"));
            store.Init("synthetic stereo plane");
            tests.Run("Import deduplicates originals and validates hints", () =>
            {
                var updates = new List<PhotoImportProgress>();
                void Observe(object value)
                {
                    var update = (PhotoImportProgress)value;
                    updates.Add(update);
                    if (update.Type == "imageImported")
                    {
                        Check(store.Load().Images.Any(image => image.Id == update.Id), "Only persisted photos count as imported");
                    }
                }
                Check(store.Import(source, "drone", 100, Observe).Count == 3, "Import three photographs");
                var revisions = store.PathFor("revisions");
                Check(Directory.GetFiles(revisions, "*.json").Length == 1
                    && File.Exists(store.PathFor($"revisions/{store.Load().Revision:D6}.json")),
                    "Archive only the final import checkpoint");
                Check(updates.Select(update => update.Completed).SequenceEqual([0, 1, 2, 3]), "Import reports progress before and after each photograph");
                Check(updates.All(update => update.Total == 3 && update.Skipped == 0), "Only supported input photographs enter the total");
                var firstEvent = JsonSerializer.SerializeToElement(updates[1], jsonCodec.Options);
                Check(firstEvent.GetProperty("type").GetString() == "imageImported" && firstEvent.GetProperty("count").GetInt32() == 1,
                    "Existing import event fields remain compatible");
                updates.Clear();
                Check(store.Import(source, "repeat", 100, Observe).Count == 0 && store.Load().Images.All(i => i.Sets.Contains("repeat")), "Deduplication and sets");
                Check(updates.Select(update => update.Completed).SequenceEqual([0, 1, 2, 3]), "Duplicate files also advance import progress");
                Check(updates[^1].Skipped == 3 && updates[^1].Count == 0, "Existing photos are counted separately from new imports");
                var revision = store.Load().Revision;
                Check(store.Import(source, "repeat", 100).Count == 0
                    && store.Load().Revision == revision && Directory.GetFiles(revisions, "*.json").Length == 2,
                    "Reimporting the same set creates no revision");
                updates.Clear();
                store.Import(source, "limited", 2, Observe);
                Check(updates.Count == 3 && updates.All(update => update.Total == 2), "The total respects the import limit");
                var images = store.Load().Images;
                Check(images.All(i => i.Gps != null && i.Attitude != null && i.FocalPixels == 250), "Sidecar calibration");
                Throws(() => store.AddHints([new Hint { Type = "tie", ImageId = images[0].Id, OtherImageId = images[1].Id, Pixel = [-1, 0], OtherPixel = [0, 0] }]), 2);
                using var lease = store.Lock("run");
                Throws(() => processor.Execute(store, "features", default), 6);
            });
            tests.Run("Interrupted photo import archives the last committed photo", () =>
            {
                var interrupted = storeFactory.Open(Path.Combine(temp, "interrupted-import"));
                interrupted.Init();
                Throws(() => interrupted.Import(source, "drone", 100, value =>
                {
                    if (value is PhotoImportProgress { Type: "imageImported" })
                    {
                        throw new EchoException("Stop after the first committed photo.", 6);
                    }
                }), 6);

                var checkpoint = interrupted.Load();
                Check(checkpoint.Images.Count == 1
                    && File.Exists(interrupted.PathFor($"revisions/{checkpoint.Revision:D6}.json")),
                    "A partial import has one photo and its archived revision");
                Check(interrupted.Import(source, "drone", 100).Count == 2 && interrupted.Load().Images.Count == 3,
                    "Retry imports only the remaining photos");
            });
            store.Edit(p => p.Settings = new Settings { DepthSuppressSky = false, OptimiseVegetation = false, TextureSuppressSky = false, FusionIslandMode = IslandTrimMode.Off, FeatureWidth = 320, FeatureLimit = 400, DepthWidth = 128, DepthPlanes = 48, DepthRangeAutomatic = false, DepthNear = 8, DepthFar = 18, DepthConcurrency = 2, TextureSize = 256, FaceLimit = 800 });
            RunData? first = null;
            tests.Run("Cancellation preserves a completed per-image checkpoint and resume reuses it", () =>
            {
                using var cancellation = new CancellationTokenSource();
                try
                {
                    processor.Execute(store, "export", cancellation.Token, output: value =>
                    {
                        using var doc = JsonDocument.Parse(jsonCodec.Encode(value));
                        if (doc.RootElement.GetProperty("type").GetString() == "imageCheckpoint")
                        {
                            cancellation.Cancel();
                        }
                    });
                    throw new InvalidOperationException("Cancellation was not observed");
                }
                catch (OperationCanceledException)
                {
                }

                var paused = store.GetRun();
                Check(paused.Status == "cancelled", "Cancelled run status");
                Check(!paused.Artifacts.ContainsKey("features"), "A partial index must not complete the feature stage");
                var partialFeatures = jsonCodec.Read<Dictionary<string, string>>(store.PathFor(paused.Artifacts["featuresPreview"] + "/units.json"));
                Check(partialFeatures.Count == 1, "Cancelled feature extraction retains its viewable image index");
                // Concurrent workers can commit more images than the coordinator has
                // published when cancellation arrives. Every such image must be reused.
                var committedFeatures = Directory.GetDirectories(store.PathFor("artifacts/feature-image"))
                    .Select(path => Path.GetRelativePath(store.Root, path)).ToHashSet(StringComparer.Ordinal);
                var reused = 0;
                var featureReports = 0;
                var depthReports = 0;
                var previewImages = partialFeatures.Count;
                var cloudProgressEvents = 0;
                var sawLiveCloudBeforeCompletion = false;
                var meshPhases = new HashSet<int>();
                var lastMeshPhase = -1;
                var alignmentPhases = new HashSet<string>();
                var lastAlignmentPhase = -1;
                var alignmentSubsteps = 0;
                first = processor.Execute(store, "export", default, paused.Id, output: value =>
                {
                    var record = JsonSerializer.SerializeToElement(value, jsonCodec.Options);
                    var payload = record.GetProperty("payload");
                    var alignmentProgress = record.GetProperty("type").GetString() == "stageProgress"
                        && payload.GetProperty("stage").GetString() == "alignment";
                    if (alignmentProgress && payload.TryGetProperty("stageStepIndex", out var alignmentPhase))
                    {
                        var index = alignmentPhase.GetInt32();
                        Check(index >= lastAlignmentPhase, "Alignment workflow phases went backwards");
                        lastAlignmentPhase = index;
                        var steps = payload.GetProperty("stageSteps");
                        alignmentPhases.Add(steps[index].GetString()!);
                        Check(steps.GetArrayLength() == 8 && steps[0].GetString() == "Load matches"
                            && steps[7].GetString() == "Save alignment", "Guided alignment covers the full workflow");
                        var persisted = store.GetRun(paused.Id).Progress!;
                        Check(persisted.StageStepIndex == index, "Live and polled alignment phases differ");
                        if (payload.TryGetProperty("stepIndex", out var substep) && substep.ValueKind == JsonValueKind.Number)
                        {
                            Check(substep.GetInt32() >= 0 && substep.GetInt32() < payload.GetProperty("steps").GetArrayLength(),
                                "Alignment operation must identify a valid substep");
                            alignmentSubsteps++;
                        }
                    }

                    var meshProgress = record.GetProperty("type").GetString() == "stageProgress"
                        && payload.GetProperty("stage").GetString() == "mesh";
                    if (meshProgress && payload.TryGetProperty("stageStepIndex", out var phase))
                    {
                        var index = phase.GetInt32();
                        Check(index >= lastMeshPhase, "Mesh phase progress went backwards");
                        lastMeshPhase = index;
                        meshPhases.Add(index);
                        var steps = payload.GetProperty("stageSteps");
                        Check(steps[0].GetString() == "Load inputs" && steps[steps.GetArrayLength() - 1].GetString() == "Save mesh", "Mesh plan must cover loading through saving");
                        Check(store.GetRun(paused.Id).Progress!.StageStepIndex == index, "Live and polled mesh phases differ");
                    }

                    var featureCheckpoint = record.GetProperty("type").GetString() == "imageCheckpoint"
                        && payload.GetProperty("stage").GetString() == "features";
                    if (featureCheckpoint)
                    {
                        featureReports++;
                        var current = store.GetRun(paused.Id);
                        var index = jsonCodec.Read<Dictionary<string, string>>(store.PathFor(current.Artifacts["featuresPreview"] + "/units.json"));
                        Check(index.Count >= previewImages, "Resuming features must not shrink the preview");
                        previewImages = index.Count;
                        var imageId = payload.GetProperty("imageId").GetString()!;
                        var features = jsonCodec.Read<FeatureSet>(store.PathFor(index[imageId] + "/features.json"));
                        Check(payload.GetProperty("pointCount").GetInt32() == features.Features.Count, "Log the saved point count");
                        Check(payload.GetProperty("lineCount").GetInt32() == features.Lines.Count, "Log the saved line count");
                        Check(payload.GetProperty("message").GetString()!.Contains(payload.GetProperty("imageName").GetString()!), "Log the photo name");
                        Check(payload.GetProperty("reused").GetBoolean() == committedFeatures.Contains(index[imageId]), "Distinguish reused and newly extracted features");
                        Check(current.Progress?.Stage == "features", "Preview writes must not replace feature progress");
                        Check(!current.Artifacts.ContainsKey("features"), "Features are viewable before stage completion");
                    }

                    var depthCheckpoint = record.GetProperty("type").GetString() == "imageCheckpoint"
                        && payload.GetProperty("stage").GetString() == "depth";
                    if (depthCheckpoint)
                    {
                        depthReports++;
                        var current = store.GetRun(paused.Id);
                        var index = jsonCodec.Read<Dictionary<string, string>>(store.PathFor(current.Artifacts["depthPreview"] + "/units.json"));
                        var imageId = payload.GetProperty("imageId").GetString()!;
                        var map = jsonCodec.Read<DepthMap>(store.PathFor(index[imageId] + "/depth.json"));
                        Check(map.ImageId == imageId, "Concurrent depth previews retain their photo identity");
                        Check(index.Count == depthReports, "Each completed depth map appears in the live index");
                        Check(payload.GetProperty("validSamples").GetInt32() == map.Depth.Count(value => float.IsFinite(value) && value > 0), "Report accepted depth coverage");
                        Check(payload.GetProperty("sampleCount").GetInt32() == map.Depth.Length, "Report the complete depth image size");
                        Check(current.Progress?.Stage == "depth", "Preview writes must not replace depth progress");
                        Check(!current.Artifacts.ContainsKey("depth"), "Depth maps are viewable before stage completion");
                    }

                    if (record.GetProperty("type").GetString() == "stageProgress"
                        && payload.GetProperty("stage").GetString() == "cloud")
                    {
                        cloudProgressEvents++;
                        Check(!string.IsNullOrWhiteSpace(payload.GetProperty("message").GetString()), "Readable dense-cloud progress");
                    }

                    if (record.GetProperty("type").GetString() == "checkpoint"
                        && payload.GetProperty("stage").GetString() == "cloudPreview")
                    {
                        var current = store.GetRun(paused.Id);
                        sawLiveCloudBeforeCompletion = !current.Artifacts.ContainsKey("cloud");
                        var previewFile = store.PathFor(current.Artifacts["cloudPreview"] + "/cloud.json");
                        Check(jsonCodec.Read<DenseCloudReport>(previewFile).Preview.Count > 0, "Live cloud preview contains points");
                    }

                    if (jsonCodec.Encode(value).Contains("artifactReused", StringComparison.Ordinal))
                    {
                        reused++;
                    }
                });
                Check(first.Status == "completed" && reused >= 1, "Resumed completed run and reused feature artifact");
                Check(featureReports == 3 && previewImages == 3, "Every image is reported and added to the live feature index");
                Check(depthReports == 3, "Every depth image is reported during processing");
                Check(cloudProgressEvents >= 3, "Dense cloud reports progress during construction");
                Check(sawLiveCloudBeforeCompletion, "Dense cloud publishes a viewable artifact before completion");
                Check(alignmentPhases.IsSupersetOf(["Load matches", "Initial alignment", "Save initial alignment", "Guided matching", "Validate recovery", "Save alignment"]),
                    "Alignment publishes initial solve, guided recovery and final saving phases");
                Check(alignmentSubsteps > 5, "The initial solve publishes its discrete operations");
                Check(meshPhases.Count >= 8 && meshPhases.Contains(0), "Mesh must publish the complete phase plan before simplification");
                var scene = jsonCodec.Read<ReconstructionScene>(store.PathFor(first.Artifacts["alignment"] + "/scene.json"));
                Check(scene.Cameras.Count == 3 && scene.Points.Count > 15, "Reconstructed camera/point geometry");
                var depths = scene.Points.Select(p => -p.Position[2]).Order().ToArray();
                Near(depths[depths.Length / 2], 12, .8);
                var export = store.PathFor(first.Artifacts["export"]);
                using var document = JsonDocument.Parse(jsonCodec.Encode(glbWriter.Inspect(Path.Combine(export, "cliff.glb"))));
                Check(document.RootElement.GetProperty("faces").GetInt32() > 20, "Nonempty photo-derived mesh");
                Check(document.RootElement.GetProperty("textures")[0].GetProperty("width").GetInt32() == 256, "GLB embeds the requested texture");
                using var atlas = SKBitmap.Decode(store.PathFor(first.Artifacts["texture"] + "/atlas.jpg"));
                Check(atlas.Width == 256 && atlas.Height == 256, "Requested atlas size");
                Check(File.Exists(Path.Combine(export, "cliff.geo.json")), "Georeference sidecar");
                Console.WriteLine($"  Synthetic export: {document.RootElement.GetProperty("faces")} triangles, {scene.Points.Count} points");
            });
            tests.Run("Disabling guided alignment removes recovery phases while reusing the initial solve", () =>
            {
                store.Edit(project => project.Settings.GuidedAlignmentPass = false);
                try
                {
                    var phases = new HashSet<string>();
                    var reusedInitial = false;
                    var single = processor.Execute(store, "alignment", default, output: value =>
                    {
                        var record = JsonSerializer.SerializeToElement(value, jsonCodec.Options);
                        var payload = record.GetProperty("payload");
                        var alignmentProgress = record.GetProperty("type").GetString() == "stageProgress"
                            && payload.GetProperty("stage").GetString() == "alignment";
                        if (alignmentProgress && payload.TryGetProperty("stageSteps", out var steps))
                        {
                            Check(steps.GetArrayLength() == 4, "Disabled recovery must not show recovery phases");
                            phases.Add(steps[payload.GetProperty("stageStepIndex").GetInt32()].GetString()!);
                        }

                        if (record.GetProperty("type").GetString() == "artifactReused")
                        {
                            reusedInitial |= jsonCodec.Encode(value).Contains("alignment-initial", StringComparison.Ordinal);
                        }
                    });
                    Check(single.Status == "completed" && reusedInitial, "The unchanged initial solve reuses its checkpoint");
                    Check(phases.SetEquals(["Load matches", "Save alignment"]), "Reused solving is not presented as newly executed work");
                }
                finally
                {
                    store.Edit(project => project.Settings.GuidedAlignmentPass = true);
                }
            });
            tests.Run("A texture-only edit preserves geometry artifacts and frozen resume inputs", () =>
            {
                Check(first != null, "Generation prerequisite");
                store.Edit(p => p.Settings.TextureSize = 512);
                var second = processor.Execute(store, "export", default);
                Check(second.Artifacts["alignment"] == first!.Artifacts["alignment"] && second.Artifacts["mesh"] == first.Artifacts["mesh"], "Geometry cache reuse");
                Check(second.Artifacts["texture"] != first.Artifacts["texture"], "Texture invalidation");
                var resumed = processor.Execute(store, "export", default, first.Id);
                Check(resumed.Snapshot.Settings.TextureSize == 256 && resumed.Artifacts["texture"] == first.Artifacts["texture"], "Resume uses immutable snapshot");
            });
            tests.Run("Subject distance edits preserve alignment, invalidate depth and reuse the original checkpoints when restored", () =>
            {
                var original = processor.Execute(store, "export", default);
                var changed = processor.Execute(store, "export", default, subjectDistanceOptions: new SubjectDistanceRunOptions
                {
                    Mode = SubjectDistanceMode.Manual,
                    MaximumDistance = 50
                });
                foreach (var stage in new[] { "features", "matching", "alignment" })
                {
                    Check(changed.Artifacts[stage] == original.Artifacts[stage], "Subject distance preserves " + stage);
                }

                foreach (var stage in new[] { "depth", "cloud", "fusion", "mesh", "texture", "export" })
                {
                    Check(changed.Artifacts[stage] != original.Artifacts[stage], "Subject distance invalidates " + stage);
                }

                var plan = jsonCodec.Read<Echo.Depth.SubjectDistancePlan>(store.PathFor(changed.Artifacts["depth"] + "/subject-distance.json"));
                Check(plan.Mode == SubjectDistanceMode.Manual && plan.MaximumDistance == 50, "The artifact records the effective subject limit");
                var restored = processor.Execute(store, "export", default);
                Check(restored.Artifacts["export"] == original.Artifacts["export"], "Restoring the setting reuses the original completed output");
            });
            tests.Run("Exclusion edits reuse cloud and rebuild downstream stages while saved runs retain their original regions", () =>
            {
                var before = store.GetRun();
                var mesh = jsonCodec.Read<Surface>(store.PathFor(before.Artifacts["mesh"] + "/mesh.json"));
                var centre = mesh.Vertices[mesh.Vertices.Count / 2];
                var voxel = jsonCodec.Read<FusedVolume>(store.PathFor(before.Artifacts["fusion"] + "/volume.json")).VoxelSize;
                foreach (var shape in Enum.GetValues<ExclusionShape>())
                {
                    var region = new ExclusionRegion
                    {
                        Shape = shape,
                        CentreX = centre[0],
                        CentreY = centre[1],
                        CentreZ = centre[2],
                        Width = voxel * 2,
                        Height = voxel * 2,
                        Depth = voxel * 2,
                        Radius = voxel,
                        Hull = shape == ExclusionShape.ProjectedHull
                            ? CropHullFixture.Box(new V3(centre), new V3(voxel * 2, voxel * 2, voxel * 2))
                            : null
                    };
                    store.Edit(project => project.Settings.ExclusionRegions = [region]);
                    var cropped = processor.Execute(store, "export", default);
                    foreach (var stage in new[] { "features", "matching", "alignment", "depth", "cloud" })
                    {
                        Check(cropped.Artifacts[stage] == before.Artifacts[stage], "Exclusions must preserve " + stage);
                    }

                    foreach (var stage in new[] { "fusion", "mesh", "texture", "export" })
                    {
                        Check(cropped.Artifacts[stage] != before.Artifacts[stage], "Exclusions must rebuild " + stage);
                    }

                    var croppedMesh = jsonCodec.Read<Surface>(store.PathFor(cropped.Artifacts["mesh"] + "/mesh.json"));
                    Check(croppedMesh.Faces.Count > 0, "Cropping a small region removed the entire mesh");
                    Check(croppedMesh.Faces.All(face => !ExclusionGeometry.Intersects(region,
                        new V3(croppedMesh.Vertices[face[0]]), new V3(croppedMesh.Vertices[face[1]]), new V3(croppedMesh.Vertices[face[2]]))),
                        "Surface completion filled an excluded region back in");
                    var resumed = processor.Execute(store, "export", default, before.Id);
                    Check(resumed.Snapshot.Settings.ExclusionRegions.Count == 0 && resumed.Artifacts["export"] == before.Artifacts["export"],
                        "Resume must retain the original regions and outputs");
                }

                store.Edit(project => project.Settings.ExclusionRegions.Clear());
            });
            tests.Run("A triangle-budget edit reuses fusion and rebuilds only the mesh and downstream artifacts", () =>
            {
                var before = store.GetRun();
                store.Edit(p => p.Settings.FaceLimit = 400);
                var changed = processor.Execute(store, "export", default);
                foreach (var stage in new[] { "alignment", "depth", "cloud", "fusion" })
                {
                    Check(changed.Artifacts[stage] == before.Artifacts[stage], "Triangle budgets preserve " + stage);
                }

                foreach (var stage in new[] { "mesh", "texture", "export" })
                {
                    Check(changed.Artifacts[stage] != before.Artifacts[stage], "Triangle budgets rebuild " + stage);
                }
            });
            tests.Run("Disabling inferred surface completion reuses fusion and produces an observed mesh", () =>
            {
                var before = store.GetRun();
                store.Edit(project => project.Settings.MeshCompleteSurface = false);
                var changed = processor.Execute(store, "export", default);
                foreach (var stage in new[] { "alignment", "depth", "cloud", "fusion" })
                {
                    Check(changed.Artifacts[stage] == before.Artifacts[stage], "Completion setting preserves " + stage);
                }

                foreach (var stage in new[] { "mesh", "texture", "export" })
                {
                    Check(changed.Artifacts[stage] != before.Artifacts[stage], "Completion setting rebuilds " + stage);
                }

                using var report = JsonDocument.Parse(File.ReadAllText(store.PathFor(changed.Artifacts["mesh"] + "/mesh.report.json")));
                Check(!report.RootElement.GetProperty("completed").GetBoolean(), "Disabled completion must not reconstruct missing surfaces");
                Check(report.RootElement.GetProperty("completionDistance").GetDouble() == 0, "Disabled completion has no extrapolation reach");
                var mesh = jsonCodec.Read<Surface>(store.PathFor(changed.Artifacts["mesh"] + "/mesh.json"));
                Check(mesh.Faces.Count > 0, "Observed geometry survives without inferred completion");
                store.Edit(project => project.Settings.MeshCompleteSurface = true);
            });
            tests.Run("Closed hull survives the final mesh and export while closure edits reuse fusion", () =>
            {
                var closed = processor.Execute(store, "export", default);
                using var report = JsonDocument.Parse(File.ReadAllText(store.PathFor(closed.Artifacts["mesh"] + "/mesh.report.json")));
                Check(report.RootElement.GetProperty("topology").GetProperty("closed").GetBoolean(), "Completed main hull has zero open edges and valid topology");
                using var exported = JsonDocument.Parse(File.ReadAllText(store.PathFor(closed.Artifacts["export"] + "/cliff.report.json")));
                Check(exported.RootElement.GetProperty("topology").GetProperty("boundaryEdges").GetInt32() == 0, "Export carries the verified hull result");
                store.Edit(project => project.Settings.MeshCloseHull = false);
                var bounded = processor.Execute(store, "export", default);
                Check(bounded.Artifacts["fusion"] == closed.Artifacts["fusion"], "Closure reuses observed fusion");
                Check(bounded.Artifacts["mesh"] != closed.Artifacts["mesh"], "Closure changes rebuild mesh geometry");
                store.Edit(project => project.Settings.MeshCloseHull = true);
                var restored = processor.Execute(store, "export", default);
                Check(restored.Artifacts["mesh"] == closed.Artifacts["mesh"], "Restoring closure reuses the correct hull checkpoint");
            });
            tests.Run("Island trimming edits reuse fusion and rebuild the completed mesh and downstream artifacts", () =>
            {
                foreach (var mode in new[] { IslandTrimMode.Small, IslandTrimMode.Main })
                {
                    var before = store.GetRun();
                    store.Edit(p => p.Settings.MeshIslandMode = mode);
                    var changed = processor.Execute(store, "export", default);
                    foreach (var stage in new[] { "alignment", "depth", "cloud", "fusion" })
                    {
                        Check(changed.Artifacts[stage] == before.Artifacts[stage], "Island trimming preserves " + stage);
                    }

                    foreach (var stage in new[] { "mesh", "texture", "export" })
                    {
                        Check(changed.Artifacts[stage] != before.Artifacts[stage], "Island trimming rebuilds " + stage);
                    }

                    using var report = JsonDocument.Parse(File.ReadAllText(store.PathFor(changed.Artifacts["mesh"] + "/mesh.report.json")));
                    var trimming = report.RootElement.GetProperty("islandTrimming");
                    Check(trimming.GetProperty("mode").GetString() == mode.ToString().ToLowerInvariant() && trimming.GetProperty("inputComponents").GetInt32() > 0,
                        "Mesh reports the requested post-completion trimming pass");
                }

                var previous = store.GetRun();
                store.Edit(p => p.Settings.MeshIslandMinimumAreaPercent = 1);
                var thresholdChanged = processor.Execute(store, "export", default);
                Check(thresholdChanged.Artifacts["fusion"] == previous.Artifacts["fusion"], "Area threshold retains fusion");
                Check(thresholdChanged.Artifacts["mesh"] != previous.Artifacts["mesh"], "Area threshold changes the mesh checkpoint");
            });
            tests.Run("Fixed spacing preserves outputs across paged RAM changes; adaptive budgets rebuild fusion", () =>
            {
                var adaptive = store.GetRun();
                store.Edit(project => project.Settings.FusionMemoryBudgetMb = project.Settings.FusionMemoryBudgetMb == 128 ? 256 : 128);
                var adaptiveBudgetChanged = processor.Execute(store, "export", default);
                Check(adaptiveBudgetChanged.Artifacts["cloud"] == adaptive.Artifacts["cloud"], "Adaptive fusion budgets preserve the dense cloud");
                Check(adaptiveBudgetChanged.Artifacts["fusion"] != adaptive.Artifacts["fusion"], "Adaptive resolution must include its candidate budget in the fingerprint");
                store.Edit(project => project.Settings.FusionVoxelSize = jsonCodec.Read<FusedVolume>(store.PathFor(adaptiveBudgetChanged.Artifacts["fusion"] + "/volume.json")).VoxelSize);
                var original = processor.Execute(store, "export", default);
                store.Edit(project => project.Settings.FusionMemoryBudgetMb = project.Settings.FusionMemoryBudgetMb == 128 ? 256 : 128);
                var budgetChanged = processor.Execute(store, "export", default);
                Check(budgetChanged.Artifacts["export"] == original.Artifacts["export"], "Paged memory limits must preserve fixed-resolution checkpoints");
                var edits = new Action<Settings>[]
                {
                    settings => settings.FusionVoxelSize *= 2,
                    settings => settings.FusionAllowCoarserResolution = true
                };
                foreach (var edit in edits)
                {
                    var before = store.GetRun();
                    store.Edit(project => edit(project.Settings));
                    var changed = processor.Execute(store, "export", default);
                    foreach (var stage in new[] { "alignment", "depth", "cloud" })
                    {
                        Check(changed.Artifacts[stage] == before.Artifacts[stage], "Fusion controls preserve " + stage);
                    }

                    foreach (var stage in new[] { "fusion", "mesh", "texture", "export" })
                    {
                        Check(changed.Artifacts[stage] != before.Artifacts[stage], "Fusion controls rebuild " + stage);
                    }
                }
            });
            tests.Run("Early island cleanup and sky sampling invalidate only their own downstream stages", () =>
            {
                var before = store.GetRun();
                store.Edit(p => p.Settings.FusionIslandMode = IslandTrimMode.Main);
                var trimmed = processor.Execute(store, "export", default);
                Check(trimmed.Artifacts["cloud"] == before.Artifacts["cloud"], "Early island cleanup reuses the dense cloud");
                Check(trimmed.Artifacts["fusion"] != before.Artifacts["fusion"] && trimmed.Artifacts["mesh"] != before.Artifacts["mesh"], "Early cleanup rebuilds fusion and meshing");
                using var report = JsonDocument.Parse(File.ReadAllText(store.PathFor(trimmed.Artifacts["fusion"] + "/fusion.report.json")));
                Check(report.RootElement.GetProperty("islandTrimming").GetProperty("mode").GetString() == "main", "Early cleanup is recorded in the fusion artifact");
                store.Edit(p => p.Settings.TextureSuppressSky = true);
                var guarded = processor.Execute(store, "export", default);
                Check(guarded.Artifacts["mesh"] == trimmed.Artifacts["mesh"], "Sky suppression preserves geometry");
                Check(guarded.Artifacts["texture"] != trimmed.Artifacts["texture"], "Sky suppression changes the baked texture checkpoint");
                store.Edit(p => p.Settings.TextureSkyMarginPixels = 16);
                var wider = processor.Execute(store, "export", default);
                Check(wider.Artifacts["mesh"] == guarded.Artifacts["mesh"] && wider.Artifacts["texture"] != guarded.Artifacts["texture"], "Sky margin affects texture only");
            });
            tests.Run("Colour finishing rebuilds texture only and off reuses the original photographic checkpoint", () =>
            {
                store.Edit(p =>
                {
                    p.Settings.TextureColourEnhancement = 0;
                    p.Settings.TextureSeamBlendPixels = 0;
                });
                var original = processor.Execute(store, "export", default);
                store.Edit(p => p.Settings.TextureColourEnhancement = .5);
                var enhanced = processor.Execute(store, "export", default);
                Check(enhanced.Artifacts["mesh"] == original.Artifacts["mesh"], "Colour finishing preserves geometry");
                Check(enhanced.Artifacts["texture"] != original.Artifacts["texture"], "Finishing changes the baked texture checkpoint");
                store.Edit(p => p.Settings.TextureSeamBlendPixels = 4);
                var softened = processor.Execute(store, "export", default);
                Check(softened.Artifacts["mesh"] == enhanced.Artifacts["mesh"] && softened.Artifacts["texture"] != enhanced.Artifacts["texture"],
                    "Seam softening changes texture without changing geometry");
                store.Edit(p =>
                {
                    p.Settings.TextureColourEnhancement = 0;
                    p.Settings.TextureSeamBlendPixels = 0;
                });
                var restored = processor.Execute(store, "export", default);
                Check(restored.Artifacts["texture"] == original.Artifacts["texture"], "Off restores the original photographic checkpoint");
            });
            tests.Run("Stage setting edits invalidate dependent artifacts while earlier work is reused", () =>
            {
                var before = store.GetRun();
                store.Edit(p => p.Settings.CloudDepthTolerance = .02);
                var cloudChanged = processor.Execute(store, "export", default);
                Check(cloudChanged.Artifacts["depth"] == before.Artifacts["depth"], "Cloud settings retain depth maps");
                foreach (var stage in new[] { "cloud", "fusion", "mesh", "texture", "export" })
                {
                    Check(cloudChanged.Artifacts[stage] != before.Artifacts[stage], "Cloud edit invalidates " + stage);
                }

                store.Edit(p => p.Settings.ExportValidate = false);
                var exportChanged = processor.Execute(store, "export", default);
                Check(exportChanged.Artifacts["texture"] == cloudChanged.Artifacts["texture"], "Export settings retain texture");
                Check(exportChanged.Artifacts["export"] != cloudChanged.Artifacts["export"], "Export setting changes export artifact");
                store.Edit(p => p.Settings.MatchRatioThreshold = .7);
                var matchingChanged = processor.Execute(store, "matching", default);
                Check(matchingChanged.Artifacts["features"] == exportChanged.Artifacts["features"], "Matching settings retain features");
                Check(matchingChanged.Artifacts["matching"] != exportChanged.Artifacts["matching"], "Matching thresholds invalidate comparisons");
            });
            tests.Run("Stage reset rebuilds units and downstream artifacts while preserving saved runs", () =>
            {
                store.Edit(project => project.Settings = first!.Snapshot.Settings);
                var before = processor.Execute(store, "export", default);
                var affected = resetService.Reset(store, "depth");
                Check(affected.SequenceEqual(new[] { "depth", "cloud", "fusion", "mesh", "texture", "export" }), "Reset lists dependencies");
                var after = processor.Execute(store, "export", default);
                Check(before.Artifacts["alignment"] == after.Artifacts["alignment"], "Reset preserves earlier checkpoints");
                foreach (var stage in affected)
                {
                    Check(before.Artifacts[stage] != after.Artifacts[stage], "Reset rebuilds " + stage);
                }

                var oldUnits = jsonCodec.Read<List<string>>(store.PathFor(before.Artifacts["depth"] + "/units.json"));
                var newUnits = jsonCodec.Read<List<string>>(store.PathFor(after.Artifacts["depth"] + "/units.json"));
                Check(!oldUnits.Intersect(newUnits).Any(), "Reset recomputes individual depth maps");
                var resumed = processor.Execute(store, "export", default, before.Id);
                Check(resumed.Artifacts["export"] == before.Artifacts["export"], "Historical resume retains original artifacts");
                using var lease = store.Lock("run");
                Throws(() => resetService.Reset(store, "features"), 6);
            });
            tests.Run("Corrupt intermediate data is rejected before reuse", () =>
            {
                var artifact = Directory.GetFiles(store.PathFor("artifacts/feature-image"), "features.json", SearchOption.AllDirectories).First();
                File.AppendAllText(artifact, " ");
                Throws(() => processor.Execute(store, "features", default), 3);
            });
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("ECHO_KEEP_TEST_OUTPUT") == "1")
            {
                Console.WriteLine("Retained test project: " + temp);
            }
            else
            {
                Directory.Delete(temp, true);
            }
        }

        return tests.Finish();
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Near(double actual, double expected, double tolerance) => Check(Math.Abs(actual - expected) < tolerance, $"Expected {expected} ± {tolerance}, got {actual}");
    private void Throws(Action action, int code)
    {
        try
        {
            action();
        }
        catch (EchoException e) when (e.ExitCode == code)
        {
            return;
        }

        throw new InvalidOperationException($"Expected EchoException with exit code {code}");
    }

    private void MakePhotos(string directory)
    {
        using var texture = new SKBitmap(900, 650);
        using var canvas = new SKCanvas(texture);
        canvas.Clear(new SKColor(50, 70, 80));
        var random = new Random(741);
        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        for (var i = 0; i < 2500; i++)
        {
            paint.Color = new SKColor((byte)random.Next(40, 240), (byte)random.Next(40, 240), (byte)random.Next(40, 240));
            canvas.DrawCircle(random.Next(900), random.Next(650), random.Next(2, 12), paint);
        }

        var origin = new GeoPosition
        {
            Latitude = -33,
            Longitude = 150,
            Altitude = 100
        };
        for (var i = 0; i < 3; i++)
        {
            var gps = new GeoPosition
            {
                Latitude = -33,
                Longitude = 150 + i * .000006,
                Altitude = 100
            };
            var cx = CameraGeometry.ToLocal(gps, origin).X;
            using var image = new SKBitmap(320, 240);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var worldX = cx + (x - 160) / 250.0 * 12;
                    var worldY = -(y - 120) / 250.0 * 12;
                    var tx = (int)(worldX * 45 + 420);
                    var ty = (int)(-worldY * 45 + 320);
                    image.SetPixel(x, y, texture.GetPixel(Math.Clamp(tx, 0, 899), Math.Clamp(ty, 0, 649)));
                }
            }

            var path = Path.Combine(directory, $"camera-{i}.png");
            rasterImageService.Save(image, path, SKEncodedImageFormat.Png);
            fileOperations.AtomicJson(path + ".echo.json", new Photo { Gps = gps, Attitude = new Attitude(), FocalPixels = 250 });
        }
    }
}
