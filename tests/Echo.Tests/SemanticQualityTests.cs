using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Meshing;
using Echo.Projects;
using Echo.SceneSemantics;
using Echo.Simplification;
using Echo.Texturing;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class SemanticQualityTests
{
    private readonly IEdgeCollapse simplifier;
    private readonly ITexturePatchBuilder charts;
    private readonly ITextureLayoutPlanner layouts;
    private readonly ICliffDepthRecovery recovery;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public SemanticQualityTests(IEdgeCollapse simplifier, ICliffDepthRecovery recovery, IProjectStoreFactory stores, IRasterImageService images, ITexturePatchBuilder charts, ITextureLayoutPlanner layouts)
    {
        this.simplifier = simplifier;
        this.charts = charts;
        this.layouts = layouts;
        this.recovery = recovery;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Semantic probabilities separate vegetation, rock and sky and preserve uncertain boundaries", () =>
        {
            var logits = Enumerable.Repeat(-20f, 150 * 4).ToArray();
            logits[4 * 4] = 20;
            logits[34 * 4 + 1] = 20;
            logits[2 * 4 + 2] = 20;
            logits[9 * 4 + 3] = 20;
            var map = SemanticProbabilities.Decode("image", 2, 2, logits);
            Check(map.Vegetation.SequenceEqual(new byte[] { 255, 0, 0, 255 }), "ADE20K vegetation labels");
            Check(map.Rock.SequenceEqual(new byte[] { 0, 255, 0, 0 }), "Rock excludes sky");
            Check(map.Sky.SequenceEqual(new byte[] { 0, 0, 255, 0 }), "Sky probabilities are retained separately");
            Check(SemanticProbabilities.ConservativeSample(map, map.Vegetation, .5, .5) == 0, "Mixed boundary stays full detail");
            Check(SemanticProbabilities.ConservativeSample(map, map.Vegetation, -1, .5) == 0, "Outside evidence is unknown");
        });
        tests.Run("Vegetation reduction removes polygons while rock and transition vertices remain fixed", () =>
        {
            var mesh = Grid();
            var labels = mesh.Vertices.Select(point => point[0] > 6).ToArray();
            var tolerances = SurfaceVegetationClassifier.Tolerances(mesh, labels, .1);
            var fixedVertices = mesh.Vertices.Where((_, index) => tolerances[index] == 0).ToArray();
            var reduced = simplifier.Reduce(mesh, mesh.Faces.Count, default, vertexTolerances: tolerances);
            Check(reduced.Faces.Count < mesh.Faces.Count - 20, "Vegetation interior should simplify");
            Check(fixedVertices.All(point => reduced.Vertices.Any(other => point.SequenceEqual(other))), "All locked vertices survive unchanged");
            Check(SurfaceTopologyBuilder.Build(reduced, default).Neighbours.Count(index => index < 0)
                == SurfaceTopologyBuilder.Build(mesh, default).Neighbours.Count(index => index < 0), "No new boundary holes");
        });
        tests.Run("Vegetation texel budget reduces chart area and preserves mixed charts", () =>
        {
            var plant = Patch(0);
            var mixed = Patch(1);
            mixed.Faces.Add(2);
            Check(TextureVegetationBudget.Apply([plant, mixed], [true, true, false], .5) == 1, "Only a pure vegetation chart is reduced");
            Check(plant.Bounds.Width * plant.Bounds.Height == 25, "Half linear resolution uses one quarter of the area");
            Check(plant.Coordinate(1, 0).X == 5 && mixed.Bounds.Width == 10, "UV coordinates and bounds scale together; mixed chart unchanged");
        });
        tests.Run("UV chart growth separates vegetation budgets without merging uncertain rock faces", () =>
        {
            var mesh = Grid();
            mesh.SourceImages = Enumerable.Repeat("", mesh.Faces.Count).ToList();
            var vegetation = mesh.Faces.Select(face => face.All(vertex => mesh.Vertices[vertex][0] > 6)).ToArray();
            var patches = charts.Build(new ProjectData(), new ReconstructionScene(), mesh, default, vegetation);
            Check(patches.Count > 1, "Different material budgets form distinct charts");
            Check(patches.All(patch => patch.Faces.All(face => vegetation[face] == vegetation[patch.Faces[0]])), "No chart crosses a material-budget boundary");
            Check(patches.Sum(patch => patch.Faces.Count) == mesh.Faces.Count, "Every triangle keeps a UV assignment");
        });
        tests.Run("Vegetation atlas allocation preserves rock density and declines excessive chart padding", () =>
        {
            var mesh = Grid();
            mesh.SourceImages = mesh.Faces.Select(_ => "").ToList();
            var project = new ProjectData();
            project.Settings.TextureSize = 256;
            project.Settings.TextureGutter = 2;
            project.Settings.TextureChartFaces = 32;
            var vegetation = mesh.Faces.Select(face => face.All(vertex => mesh.Vertices[vertex][0] > 6)).ToArray();
            var layout = layouts.Plan(project, new ReconstructionScene(), mesh, default, vegetationFaces: vegetation);
            Check(layout.VegetationChartCount > 0, "Vegetation regions receive a smaller texture budget");
            Check(layout.TexelsPerWorldUnit >= layout.BaselineTexelsPerWorldUnit, "Rock density cannot regress");
            Check(layout.Patches.Sum(patch => patch.Faces.Count) == mesh.Faces.Count, "Every triangle remains textured");

            project.Settings.TextureGutter = 16;
            project.Settings.TextureChartFaces = 512;
            var fragmented = Enumerable.Range(0, mesh.Faces.Count).Select(face => face % 2 == 0).ToArray();
            var fallback = layouts.Plan(project, new ReconstructionScene(), mesh, default, vegetationFaces: fragmented);
            Check(fallback.VegetationChartCount == 0, "Unpackable fragmented vegetation keeps the valid original layout");
            Check(fallback.TexelsPerWorldUnit == fallback.BaselineTexelsPerWorldUnit, "Padding fallback preserves full detail");
        });
        tests.Run("Vegetation labels need independent visible depth agreement and reject rock disagreement", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            var semantics = new Dictionary<string, SemanticMap>();
            for (var index = 0; index < 3; index++)
            {
                var id = index.ToString();
                project.Images.Add(new Photo { Id = id, Width = 32, Height = 32, FocalPixels = 32 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [index - 1, 0, 0] });
                maps.Add(new DepthMap { ImageId = id, Width = 32, Height = 32, Depth = Enumerable.Repeat(10f, 1024).ToArray() });
                semantics.Add(id, new SemanticMap { ImageId = id, Width = 4, Height = 4, Vegetation = Enumerable.Repeat((byte)255, 16).ToArray(), Rock = new byte[16] });
            }
            var surface = new Surface { Vertices = [[0, 0, 10]] };
            Check(SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default)[0], "Three consistent vegetation views");
            semantics["2"].Rock = Enumerable.Repeat((byte)255, 16).ToArray();
            Check(!SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default)[0], "Confident rock vetoes simplification");
            maps[2].Depth = Enumerable.Repeat(2f, 1024).ToArray();
            Check(SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default)[0], "Occluded rock evidence must not vote");
            maps[1].Depth = new float[1024];
            Check(!SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default)[0], "One visible view is insufficient");
        });
        tests.Run("Completed vegetation uses visible photographs but cannot borrow labels through an occluder", () =>
        {
            var project = new ProjectData();
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            var semantics = new Dictionary<string, SemanticMap>();
            foreach (var id in new[] { "left", "right" })
            {
                project.Images.Add(new Photo { Id = id, Width = 32, Height = 32, FocalPixels = 32 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [id == "left" ? -1 : 1, 0, 0] });
                maps.Add(new DepthMap { ImageId = id, Width = 32, Height = 32, Depth = new float[1024] });
                semantics.Add(id, new SemanticMap
                {
                    ImageId = id,
                    Width = 4,
                    Height = 4,
                    Vegetation = Enumerable.Repeat((byte)255, 16).ToArray(),
                    Rock = new byte[16]
                });
            }
            var surface = new Surface
            {
                Vertices = [[-.5, -.5, 10], [.5, -.5, 10], [0, .5, 10]],
                Faces = [[0, 1, 2]]
            };
            Check(SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default).All(value => value),
                "Two visible photographs can classify an already completed patch");
            surface.Vertices = [.. surface.Vertices, [-5, -5, 5], [5, -5, 5], [0, 5, 5]];
            surface.Faces.Add([3, 4, 5]);
            Check(SurfaceVegetationClassifier.Classify(surface, project, scene, maps, semantics, default).Take(3).All(value => !value),
                "Occluded vegetation labels cannot simplify the hidden surface");
        });
        tests.Run("Cliff recovery uses fine stereo detail, preserves existing samples and refuses unsupported gaps", VerifyRecovery);
    }

    private void VerifyRecovery()
    {
        var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-cliff-recovery-" + Guid.NewGuid().ToString("N")));
        store.Init();
        try
        {
            var project = new ProjectData { Settings = new Settings { DepthSuppressSky = true } };
            var scene = new ReconstructionScene();
            var maps = new List<DepthMap>();
            using var texture = new SKBitmap(320, 192);
            var random = new Random(43);
            for (var y = 0; y < texture.Height; y++)
            {
                for (var x = 0; x < texture.Width; x++)
                {
                    var value = (byte)random.Next(30, 220);
                    texture.SetPixel(x, y, new SKColor(value, value, value));
                }
            }
            foreach (var shift in new[] { 0, -12, 12 })
            {
                var id = shift.ToString();
                using var image = new SKBitmap(320, 192);
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        image.SetPixel(x, y, texture.GetPixel(Math.Clamp(x + shift, 0, 319), y));
                    }
                }
                images.Save(image, store.PathFor(id + ".png"), SKEncodedImageFormat.Png);
                project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = 320, Height = 192, FocalPixels = 240 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [shift / 24.0, 0, 0] });
                maps.Add(new DepthMap
                {
                    ImageId = id,
                    Width = 160,
                    Height = 96,
                    Depth = Enumerable.Repeat(10f, 160 * 96).ToArray(),
                    Confidence = Enumerable.Repeat(.9f, 160 * 96).ToArray()
                });
            }
            var source = maps[0];
            for (var y = 45; y < 49; y++)
            {
                for (var x = 78; x < 82; x++)
                {
                    source.Depth[y * 160 + x] = 0;
                    source.Confidence[y * 160 + x] = 0;
                }
            }
            var semantics = new SemanticMap
            {
                ImageId = "0",
                Width = 16,
                Height = 16,
                Rock = Enumerable.Repeat((byte)255, 256).ToArray(),
                Vegetation = new byte[256]
            };
            var recovered = recovery.Recover(store, project, scene, source, maps, semantics, default);
            Check(recovered.SourceImageWidth == 320 && recovered.RecoveredSamples >= 12, "Fine photographs should recover the interior gap");
            for (var index = 0; index < source.Depth.Length; index++)
            {
                if (source.Depth[index] > 0)
                {
                    Check(source.Depth[index] == recovered.Map.Depth[index] && source.Confidence[index] == recovered.Map.Confidence[index], "Original samples must remain bitwise unchanged");
                }
                else if (recovered.Map.Depth[index] > 0)
                {
                    Check(Math.Abs(recovered.Map.Depth[index] - 10) < .03, "Recovered samples agree with the known ten-metre plane");
                }
            }
            semantics.Sky = Enumerable.Repeat((byte)255, 256).ToArray();
            Check(recovery.Recover(store, project, scene, source, maps, semantics, default).RecoveredSamples == 0, "Sky veto prevents recovery from refilling masked pixels even with conflicting rock labels");
            semantics.Sky = [];
            maps[2].Depth = new float[source.Depth.Length];
            Check(recovery.Recover(store, project, scene, source, maps, semantics, default).RecoveredSamples == 0, "A single confirming view cannot fill a gap");
            maps[2].Depth = Enumerable.Repeat(10f, source.Depth.Length).ToArray();
            var unsupported = new DepthMap { Width = 160, Height = 96, Depth = (float[])source.Depth.Clone() };
            for (var y = 40; y < 55; y++)
            {
                for (var x = 78; x < 90; x++)
                {
                    unsupported.Depth[y * 160 + x] = 0;
                }
            }
            Check(CliffRecoverySearch.Anchor(unsupported, 79, 46) == 0, "One-sided silhouettes cannot seed recovery");
            using var blank = new SKBitmap(320, 192);
            blank.Erase(new SKColor(127, 127, 127));
            images.Save(blank, store.PathFor("0.png"), SKEncodedImageFormat.Png);
            Check(recovery.Recover(store, project, scene, source, maps, semantics, default).RecoveredSamples == 0, "Depth priors alone must not fabricate textureless geometry");
            semantics.Rock = new byte[256];
            Check(recovery.Recover(store, project, scene, source, maps, semantics, default).SearchedSamples == 0, "Unknown/vegetation regions are not cliff targets");
        }
        finally
        {
            Directory.Delete(store.Root, true);
        }
    }

    private Surface Grid()
    {
        var mesh = new Surface();
        for (var y = 0; y <= 12; y++)
        {
            for (var x = 0; x <= 12; x++)
            {
                mesh.Vertices.Add([x, y, .02 * Math.Sin(x) * Math.Sin(y)]);
            }
        }
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                var a = y * 13 + x;
                mesh.Faces.Add([a, a + 1, a + 13]);
                mesh.Faces.Add([a + 1, a + 14, a + 13]);
            }
        }
        return mesh;
    }

    private TexturePatch Patch(int face)
    {
        var patch = new TexturePatch { Bounds = new SKRect(0, 0, 10, 10) };
        patch.Faces.Add(face);
        patch.Coordinates.Add(0, [0, 0]);
        patch.Coordinates.Add(1, [10, 10]);
        return patch;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
