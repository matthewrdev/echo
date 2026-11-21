using System.Text.Json;
using Echo.Models;
using Echo.Geometry;
using Echo.Texturing;

namespace Echo.Tests;

public sealed class TexturePriorityTests
{
    private readonly ITextureLayoutPlanner planner;

    public TexturePriorityTests(ITextureLayoutPlanner planner)
    {
        this.planner = planner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Texture painting round trips, validates brush geometry and rejects another alignment", () =>
        {
            var settings = new Settings { TexturePriority = Painting() };
            var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
            restored.Validate();
            Check(restored.TexturePriority!.Strokes[0].Samples[0].Radius == 2, "Brush geometry survives persistence");
            Check(JsonSerializer.Deserialize<Settings>("{}")!.TexturePriority == null, "Old projects remain unpainted");
            restored.TexturePriority.ValidateAlignment("alignment-a");
            Expect<EchoException>(() => restored.TexturePriority.ValidateAlignment("alignment-b"));
            restored.TexturePriority.Strokes[0].Samples[0].Normal = [0, 0, 0];
            Expect<EchoException>(restored.Validate);
            restored.TexturePriority = Painting();
            restored.TexturePriority.Strokes[0].Weight = 0;
            Expect<EchoException>(restored.Validate);
            restored.TexturePriority = Painting();
            restored.TexturePriority.Strokes[0].Samples[0].Centre = [double.NaN, 0, 0];
            Expect<EchoException>(restored.Validate);
        });
        tests.Run("Brush falloff, shallow depth, overlapping samples and neutral erasing agree", () =>
        {
            var painting = Painting();
            var field = new TexturePriorityField(painting, default);
            var influences = new SortedDictionary<int, double>();
            Check(field.LogWeight(new V3(0, 0, 0), influences) == 2, "High priority reaches its target");
            Check(Math.Abs(field.LogWeight(new V3(1.5, 0, 0), influences) - 1) < 1e-10, "Half-strength feather is smooth");
            Check(field.LogWeight(new V3(0, 0, 1), influences) == 0, "A surface behind the brush remains unchanged");
            painting.Strokes[0].Samples.Add(painting.Strokes[0].Samples[0]);
            field = new TexturePriorityField(painting, default);
            Check(Math.Abs(field.LogWeight(new V3(1.5, 0, 0), influences) - 1) < 1e-10, "Overlapping stamps do not amplify one stroke");
            painting.Strokes.Add(new TexturePriorityStroke { Weight = 1, Samples = [painting.Strokes[0].Samples[0]] });
            field = new TexturePriorityField(painting, default);
            Check(field.LogWeight(new V3(0, 0, 0), influences) == 0, "Neutral eraser restores the default budget");
        });
        tests.Run("Painted chart budgets increase relative texel area and retain every mesh face", () =>
        {
            var mesh = Mesh();
            var project = new ProjectData { Settings = new Settings { ComputeBackend = ComputeBackend.Cpu, TextureSize = 256 } };
            var baseline = planner.Plan(project, new ReconstructionScene(), mesh, default);
            project.Settings.TexturePriority = Painting();
            var painted = planner.Plan(project, new ReconstructionScene(), mesh, default);
            var bands = TexturePriorityBudget.Classify(mesh, project.Settings.TexturePriority, default)!;
            Check(bands.SequenceEqual([2, 2, 0, 0]), "Guidance maps to the intended surface");
            Check(painted.Patches.SelectMany(patch => patch.Faces).Order().SequenceEqual(Enumerable.Range(0, mesh.Faces.Count)), "Exactly one chart assignment per face");
            var high = painted.Patches.Single(patch => patch.Faces.Contains(0));
            var normal = painted.Patches.Single(patch => patch.Faces.Contains(2));
            Check(Math.Abs(Area(high) / Area(normal) - 4) < .01, "4× priority receives 4× the UV area for equal surface area");
            var baseHigh = baseline.Patches.Single(patch => patch.Faces.Contains(0));
            Check(Area(high) * Math.Pow(painted.TexelsPerWorldUnit, 2) > Area(baseHigh) * Math.Pow(baseline.TexelsPerWorldUnit, 2), "Priority increases actual packed detail");
            foreach (var patch in painted.Patches)
            {
                Check(patch.AtlasBounds.Left >= 4 && patch.AtlasBounds.Top >= 4, "Gutters remain reserved");
                Check(patch.AtlasBounds.Right <= 252 && patch.AtlasBounds.Bottom <= 252, "Charts stay inside the atlas");
            }
            Expect<OperationCanceledException>(() => TexturePriorityBudget.Classify(mesh, project.Settings.TexturePriority, new CancellationToken(true)));
        });
        tests.Run("Adjacent priority bands remain separate through chart merging and vegetation budgeting", () =>
        {
            var mesh = new Surface
            {
                Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                Faces = [[0, 1, 2], [0, 2, 3]],
                SourceImages = ["", ""]
            };
            var project = new ProjectData { Settings = new Settings { ComputeBackend = ComputeBackend.Cpu, TextureSize = 256 } };
            project.Settings.TexturePriority = new TexturePriorityPainting
            {
                AlignmentArtifact = "alignment-a",
                Strokes = [new TexturePriorityStroke
                {
                    Weight = 4,
                    Samples = [new TextureBrushSample { Centre = [2d / 3, 1d / 3, 0], Radius = .4, Depth = .1 }]
                }]
            };
            var bands = TexturePriorityBudget.Classify(mesh, project.Settings.TexturePriority, default)!;
            Check(bands[0] > bands[1], "Centroid captures a local brush on a coarse mesh");
            var layout = planner.Plan(project, new ReconstructionScene(), mesh, default, vegetationFaces: [false, true]);
            Check(layout.Patches.Count == 2, "Chart merging cannot erase the priority boundary");
            Check(Area(layout.Patches.Single(patch => patch.Faces.Contains(0))) > Area(layout.Patches.Single(patch => patch.Faces.Contains(1))), "Painted guidance survives vegetation optimisation");
        });
        tests.Run("Brush spatial index preserves ordered painting across distant and overlapping stamps", () =>
        {
            var painting = Painting();
            painting.Strokes[0].Samples.AddRange(Enumerable.Range(1, 64).Select(index => new TextureBrushSample { Centre = [index * 4, 0, 0], Radius = 2, Depth = .5 }));
            painting.Strokes.Add(new TexturePriorityStroke { Weight = .25, Samples = [new TextureBrushSample { Centre = [128, 0, 0], Radius = 2, Depth = .5 }] });
            var field = new TexturePriorityField(painting, default);
            var influences = new SortedDictionary<int, double>();
            Check(field.LogWeight(new V3(128, 0, 0), influences) == -2, "Later strokes win after spatial reordering");
            Check(field.LogWeight(new V3(252, 0, 0), influences) == 2, "Index retains distant stamps");
            Check(field.LogWeight(new V3(1000, 0, 0), influences) == 0, "Outside the painting stays neutral");
        });
    }

    private TexturePriorityPainting Painting() => new()
    {
        AlignmentArtifact = "alignment-a",
        Strokes = [new TexturePriorityStroke { Weight = 4, Samples = [new TextureBrushSample { Radius = 2, Depth = .5 }] }]
    };

    private Surface Mesh() => new()
    {
        Vertices = [[-.25, -.25, 0], [.25, -.25, 0], [.25, .25, 0], [-.25, .25, 0],
            [3.75, -.25, 0], [4.25, -.25, 0], [4.25, .25, 0], [3.75, .25, 0]],
        Faces = [[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]],
        SourceImages = ["", "", "", ""]
    };

    private double Area(TexturePatch patch) => patch.Triangles.Sum(triangle =>
    {
        var a = patch.Coordinates[triangle[0]];
        var b = patch.Coordinates[triangle[1]];
        var c = patch.Coordinates[triangle[2]];
        return Math.Abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])) / 2;
    });

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
