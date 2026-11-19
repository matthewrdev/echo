using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Meshing;
using Echo.Validation;

namespace Echo.Tests;

[Export]
public sealed class ReconstructionValidationTests
{
    private readonly IJsonCodec json;

    [ImportingConstructor]
    public ReconstructionValidationTests(IJsonCodec json)
    {
        this.json = json;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Pinned reconstruction cases decode with complete limits and explicit source hashes", () =>
        {
            foreach (var name in new[] { "can-lolo", "northern-buttress", "je-taime" })
            {
                var fixture = json.Read<ReconstructionCase>(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reconstruction", name + ".json"));
                Check(fixture.Name == name, "The fixture retains its identity");
                Check(fixture.PhotoHashes.Count >= 3 && fixture.PhotoHashes.Distinct().Count() == fixture.PhotoHashes.Count, "Pinned originals are distinct");
                Check(fixture.PhotoHashes.All(hash => hash.Length == 64 && hash.All(Uri.IsHexDigit)), "Each original has a SHA-256 hash");
                fixture.Settings.Validate();
                QualityGate.Validate(fixture.Limits);
                Check(fixture.Limits[QualityMetric.MainHullBoundaryEdges].Maximum == 0, "Main hull closure is an acceptance requirement");
                Check(json.Clone(fixture.Settings).MeshCloseHull, "Cloned pipeline settings retain hull closure");
                Check(fixture.Limits[QualityMetric.PhotographedColourArea].Minimum > 0,
                    "Closing unseen surfaces cannot discard the photograph-covered area");
            }
        });
        tests.Run("Reconstruction gates reject missing, nonfinite and regressed measurements", () =>
        {
            var limits = Enum.GetValues<QualityMetric>().ToDictionary(metric => metric, _ => new MetricBounds(1, 2));
            var measurements = limits.Keys.ToDictionary(metric => metric, _ => 1.5);
            Check(QualityGate.Evaluate(measurements, limits).All(check => check.Passed), "Within reviewed intervals");
            measurements.Remove(QualityMetric.DenseSamples);
            measurements[QualityMetric.MeshArea] = double.NaN;
            measurements[QualityMetric.MeanDepthCoverage] = 0;
            measurements[QualityMetric.MedianReprojectionErrorPixels] = double.PositiveInfinity;
            var failures = QualityGate.Evaluate(measurements, limits).Where(check => !check.Passed).ToArray();
            Check(failures.Length == 4, "Missing and invalid data cannot quietly pass");
            Check(failures.Count(check => check.Actual == null) == 3, "Unavailable numbers remain serializable as null");
            limits.Remove(QualityMetric.MeshArea);
            Expect<InvalidDataException>(() => QualityGate.Validate(limits));
            limits[QualityMetric.MeshArea] = new MetricBounds(2, 1);
            Expect<InvalidDataException>(() => QualityGate.Validate(limits));
        });
        tests.Run("Validation memory falls back to sampled native-worker usage without inventing a zero peak", () =>
        {
            var result = new ValidationResult();
            Check(result.PeakWorkingSetBytes == null, "Unavailable memory remains unknown");
            result.PeakRegisteredWorkingSetBytes = 4096;
            Check(result.PeakWorkingSetBytes == 4096, "Sampled process and worker usage survives an unavailable OS counter");
            result.PeakProcessWorkingSetBytes = 8192;
            Check(result.PeakWorkingSetBytes == 8192, "The larger available peak wins");
            result.PeakRegisteredWorkingSetBytes = 16384;
            Check(result.PeakWorkingSetBytes == 16384, "Native workers are not dropped from the reported peak");
        });
        tests.Run("Main hull topology welds photograph seams without hiding missing triangles", () =>
        {
            var tetrahedron = Tetrahedron();
            Check(SurfaceTopology.Measure(tetrahedron, default).Closed, "A complete tetrahedron is closed");
            var split = new Surface();
            foreach (var face in tetrahedron.Faces)
            {
                var first = split.Vertices.Count;
                foreach (var vertex in face)
                {
                    split.Vertices.Add(tetrahedron.Vertices[vertex].ToArray());
                }
                split.Faces.Add([first, first + 1, first + 2]);
            }
            Check(SurfaceTopology.Measure(split, default).Closed, "Texture seams do not become holes");
            split.Faces.RemoveAt(0);
            var open = SurfaceTopology.Measure(split, default);
            Check(!open.Closed && open.BoundaryEdges == 3, "A missing triangle remains a real boundary");
            Check(open.Boundaries.Count == 1, "The missing face makes one boundary cycle");
            var reversed = Tetrahedron();
            Array.Reverse(reversed.Faces[0]);
            var inconsistent = SurfaceTopology.Measure(reversed, default);
            Check(inconsistent.BoundaryEdges == 0 && inconsistent.WindingConflicts == 3 && !inconsistent.Closed,
                "Zero open edges do not hide inconsistent winding");
            var collapsed = Tetrahedron();
            collapsed.Vertices[3] = [.5, 0, 0];
            var degenerate = SurfaceTopology.Measure(collapsed, default);
            Check(degenerate.DegenerateFaces > 0 && !degenerate.Closed, "Zero-area triangles cannot pass as a valid hull");
        });
        tests.Run("Main hull topology distinguishes detached fragments and nonmanifold edges", () =>
        {
            var surface = Tetrahedron();
            surface.Vertices.Add([10, 0, 0]);
            surface.Vertices.Add([10.01, 0, 0]);
            surface.Vertices.Add([10, .01, 0]);
            surface.Faces.Add([4, 5, 6]);
            var main = SurfaceTopology.Measure(surface, default);
            Check(main.Closed && main.MainHullFaces == 4, "A tiny detached fragment is not the main hull");
            surface.Faces.Add(surface.Faces[0].ToArray());
            var nonmanifold = SurfaceTopology.Measure(surface, default);
            Check(!nonmanifold.Closed && nonmanifold.NonManifoldEdges == 3, "Duplicate faces do not fake a closed manifold");
            Expect<OperationCanceledException>(() => SurfaceTopology.Measure(surface, new CancellationToken(true)));
        });
        tests.Run("Validation inputs cannot escape the selected dataset", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-validation-paths");
            Expect<InvalidDataException>(() => ValidationPaths.ContainedPath(root, "../outside.jpg"));
            Expect<InvalidDataException>(() => ValidationPaths.ContainedPath(root, Path.GetFullPath("outside.jpg")));
            Check(ValidationPaths.ContainedPath(root, "photos/original.jpg").StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Nested originals stay inside their dataset");
        });
    }

    private Surface Tetrahedron() => new()
    {
        Vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]],
        Faces = [[0, 2, 1], [0, 1, 3], [1, 2, 3], [2, 0, 3]]
    };

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
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
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
