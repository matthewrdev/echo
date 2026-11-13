using System.ComponentModel.Composition;
using System.Globalization;
using System.Text.Json;
using Echo.Contracts;
using Echo.Geometry;
using SkiaSharp;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ReconstructionTests
{
    private readonly Echo.Geometry.ICameraGeometry cameraGeometry;
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
    private readonly Echo.Pipeline.IStageResetService resetService;
    [ImportingConstructor]
    public ReconstructionTests(Echo.Geometry.ICameraGeometry cameraGeometry, Echo.Imaging.IRasterImageService rasterImageService, Echo.Infrastructure.IJsonCodec jsonCodec, Echo.Infrastructure.IFileOperations fileOperations, Echo.Reconstruction.IReconstructor reconstructor, Echo.Optimization.IBundleAdjuster bundleAdjuster, Echo.Meshing.IMeshBuilder meshBuilder, Echo.Simplification.IEdgeCollapse edgeCollapse, Echo.Export.IGlbWriter glbWriter, Echo.Projects.IProjectStoreFactory storeFactory, Echo.Pipeline.IProcessor processor, ProcessingSettingsTests settingsTests, TextureTests textureTests, Echo.Pipeline.IStageResetService resetService, ProcessingEfficiencyTests efficiencyTests, LineFeatureTests lineTests, ResetCommandTests resetCommandTests, FeatureExtractionTests featureExtractionTests, CloudProcessingTests cloudProcessingTests)
    {
        this.cameraGeometry = cameraGeometry;
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
        tests.Run("MEF composes all processing stages in dependency order", () =>
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
            Check(processor.Stages.SequenceEqual(expected), "Incomplete or incorrectly ordered MEF composition");
        });
        tests.Run("WGS84 axes and DJI camera convention", () =>
        {
            var origin = new GeoPosition
            {
                Latitude = -33,
                Longitude = 150,
                Altitude = 100
            };
            var east = cameraGeometry.ToLocal(new GeoPosition { Latitude = -33, Longitude = 150.00001, Altitude = 100 }, origin);
            var north = cameraGeometry.ToLocal(new GeoPosition { Latitude = -32.99999, Longitude = 150, Altitude = 100 }, origin);
            var up = cameraGeometry.ToLocal(new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 101 }, origin);
            Check(east.X > .8 && Math.Abs(east.Z) < .01, "East axis");
            Check(north.Z < -1 && Math.Abs(north.X) < .01, "North is -Z");
            Near(up.Y, 1, 1e-6);
            var matrix = cameraGeometry.LocalToEcefMatrix(origin);
            var ecef = cameraGeometry.Ecef(origin);
            Near(matrix[3], ecef.X, 1e-8);
            Near(matrix[7], ecef.Y, 1e-8);
            Near(matrix[11], ecef.Z, 1e-8);
            var testGeo = new GeoPosition
            {
                Latitude = -32.99999,
                Longitude = 150.00001,
                Altitude = 104
            };
            var local = cameraGeometry.ToLocal(testGeo, origin);
            var expected = cameraGeometry.Ecef(testGeo);
            Near(matrix[0] * local.X + matrix[1] * local.Y + matrix[2] * local.Z + matrix[3], expected.X, 1e-6);
            Near(matrix[4] * local.X + matrix[5] * local.Y + matrix[6] * local.Z + matrix[7], expected.Y, 1e-6);
            Near(matrix[8] * local.X + matrix[9] * local.Y + matrix[10] * local.Z + matrix[11], expected.Z, 1e-6);
            var rotation = cameraGeometry.AttitudeRotation(new Attitude { Yaw = 90, Pitch = -30 });
            var right = cameraGeometry.Rotate(rotation, new V3(1, 0, 0));
            var down = cameraGeometry.Rotate(rotation, new V3(0, 1, 0));
            var forward = cameraGeometry.Rotate(rotation, new V3(0, 0, 1));
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
                A = cameraGeometry.Project(image, a, point)![..2],
                B = cameraGeometry.Project(image, b, point)![..2]
            };
            var recovered = cameraGeometry.Triangulate(image, image, a, b, match);
            Check(recovered != null && (recovered.Point - point).Length < 1e-8, "Metric triangulation");
            Check(cameraGeometry.Triangulate(image, image, a, b, new Match { A = [400, 300], B = [600, 300] }) == null, "Behind camera rejection");
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
                matches.Add(new Match { A = cameraGeometry.Project(image, a, point)![..2], B = cameraGeometry.Project(image, b, point)![..2] });
            }

            for (var i = 0; i < 12; i++)
            {
                matches.Add(new Match { A = [random.Next(1000), random.Next(700)], B = [random.Next(1000), random.Next(700)] });
            }

            var result = cameraGeometry.EstimateRelative(image, image, matches);
            Check(result != null && result.Inliers.Count >= 55, "Visual relative pose");
            var centre = cameraGeometry.Rotate(cameraGeometry.Transpose(result!.Rotation), new V3(result.Translation)) * -1;
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
                Rotation = cameraGeometry.AttitudeRotation(new Attitude())
            };
            var cb = new CameraPose
            {
                ImageId = "b",
                Centre = cameraGeometry.ToLocal(gpsB, origin).Array,
                Rotation = ca.Rotation
            };
            var cg = new CameraPose
            {
                ImageId = "ground",
                Centre = [1, .4, -.2],
                Rotation = cameraGeometry.AttitudeRotation(new Attitude { Yaw = 5, Pitch = -3 })
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
                ab.Matches.Add(new Match { A = cameraGeometry.Project(a, ca, point)![..2], B = cameraGeometry.Project(b, cb, point)![..2] });
                bg.Matches.Add(new Match { A = cameraGeometry.Project(b, cb, point)![..2], B = cameraGeometry.Project(ground, cg, point)![..2] });
            }

            var result = reconstructor.Align(new ProjectData { Images = [a, b, ground] }, [ab, bg], default);
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
                    Rotation = cameraGeometry.AttitudeRotation(image.Attitude),
                    FocalPixels = 850
                };
                truth.Add(pose);
                scene.Cameras.Add(new CameraPose { ImageId = image.Id, Centre = i == 0 ? pose.Centre : [i * 1.2 + .25 * Math.Sin(i), i * .12 + .2 * Math.Cos(i), .15 * Math.Sin(i)], Rotation = cameraGeometry.AttitudeRotation(new Attitude { Yaw = i * 3 + (i == 0 ? 0 : 1.5 * Math.Cos(i)), Pitch = i == 0 ? 0 : .9 * Math.Sin(i) }) });
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
                        pair.Matches.Add(new Match { A = cameraGeometry.Project(project.Images[a], truth[a], point)![..2], B = cameraGeometry.Project(project.Images[b], truth[b], point)![..2] });
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

                Near(area, 29 * 19, .001);
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
                Check(store.Import(source, "drone", 100).Count == 3, "Import three photographs");
                Check(store.Import(source, "repeat", 100).Count == 0 && store.Load().Images.All(i => i.Sets.Contains("repeat")), "Deduplication and sets");
                var images = store.Load().Images;
                Check(images.All(i => i.Gps != null && i.Attitude != null && i.FocalPixels == 250), "Sidecar calibration");
                Throws(() => store.AddHints([new Hint { Type = "tie", ImageId = images[0].Id, OtherImageId = images[1].Id, Pixel = [-1, 0], OtherPixel = [0, 0] }]), 2);
                using var lease = store.Lock("run");
                Throws(() => processor.Execute(store, "features", default), 6);
            });
            store.Edit(p => p.Settings = new Settings { FeatureWidth = 320, FeatureLimit = 400, DepthWidth = 80, DepthPlanes = 48, DepthNear = 8, DepthFar = 18, TextureSize = 256, FaceLimit = 800 });
            RunData? first = null;
            tests.Run("Cancellation preserves a completed per-image checkpoint and resume reuses it", () =>
            {
                using var cancellation = new CancellationTokenSource();
                try
                {
                    processor.Execute(store, "export", cancellation.Token, output: value =>
                    {
                        using var doc = JsonDocument.Parse(jsonCodec.Encode(value));
                        if (doc.RootElement.GetProperty("type").GetString() == "artifactCommitted")
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
                var reused = 0;
                var cloudProgressEvents = 0;
                var sawLiveCloudBeforeCompletion = false;
                first = processor.Execute(store, "export", default, paused.Id, output: value =>
                {
                    var record = JsonSerializer.SerializeToElement(value, jsonCodec.Options);
                    var payload = record.GetProperty("payload");
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
                Check(cloudProgressEvents >= 3, "Dense cloud reports progress during construction");
                Check(sawLiveCloudBeforeCompletion, "Dense cloud publishes a viewable artifact before completion");
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
            var cx = cameraGeometry.ToLocal(gps, origin).X;
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
