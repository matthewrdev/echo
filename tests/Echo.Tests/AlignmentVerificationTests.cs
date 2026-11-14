using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Reconstruction;

namespace Echo.Tests;

[Export]
public sealed class AlignmentVerificationTests
{
    private readonly IReconstructor reconstructor;

    [ImportingConstructor]
    public AlignmentVerificationTests(IReconstructor reconstructor)
    {
        this.reconstructor = reconstructor;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Rejected matches cannot bias initial cameras or appear in the first point cloud", () =>
        {
            var origin = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 100 };
            var rightGps = new GeoPosition { Latitude = -33, Longitude = 150.00002, Altitude = 100 };
            var left = new Photo { Id = "left", Width = 1000, Height = 700, FocalPixels = 850, Gps = origin, Attitude = new Attitude() };
            var right = new Photo { Id = "right", Width = 1000, Height = 700, FocalPixels = 850, Gps = rightGps, Attitude = new Attitude() };
            var leftPose = new CameraPose { Rotation = CameraGeometry.AttitudeRotation(left.Attitude) };
            var rightPose = new CameraPose { Rotation = leftPose.Rotation, Centre = CameraGeometry.ToLocal(rightGps, origin).Array };
            var pair = new ImagePair { A = left.Id, B = right.Id };
            var random = new Random(812);
            for (var index = 0; index < 70; index++)
            {
                var point = new V3(random.NextDouble() * 4 - 2, random.NextDouble() * 3 - 1, -8 - random.NextDouble() * 8);
                pair.Matches.Add(new Match { A = CameraGeometry.Project(left, leftPose, point)![..2], B = CameraGeometry.Project(right, rightPose, point)![..2] });
            }
            var project = new ProjectData { Images = [left, right] };
            var clean = reconstructor.Align(project, [pair], default);
            var wrong = new List<Match>();
            for (var index = 0; index < 20; index++)
            {
                var source = pair.Matches[index];
                // These aliases pass the old loose 18-pixel triangulation gate.
                var match = new Match { A = source.A, B = [source.B[0] + 15, source.B[1] + 14], GeometricInlier = true };
                wrong.Add(match);
                pair.Matches.Add(match);
            }
            var previews = 0;
            var contaminated = reconstructor.Align(project, [pair], default, preview =>
            {
                previews++;
                foreach (var point in preview.Points)
                {
                    if (wrong.Exists(match => ReferenceEquals(match.B, point.PixelB)))
                    {
                        throw new InvalidOperationException("Rejected alias leaked into initialization preview");
                    }
                }
            });
            Check(previews > 0 && wrong.TrueForAll(match => match.GeometricInlier == false), "All known aliases must be rechecked, even with stale accepted flags");
            Check(clean.Points.Count == contaminated.Points.Count, "Rejected candidates cannot change the initial cloud");
            for (var camera = 0; camera < clean.Cameras.Count; camera++)
            {
                for (var entry = 0; entry < 9; entry++)
                {
                    Check(Math.Abs(clean.Cameras[camera].Rotation[entry] - contaminated.Cameras[camera].Rotation[entry]) < 1e-9, "Rejected candidates biased initial rotation");
                }
            }
            var reduced = new ImagePair { A = left.Id, B = right.Id, Matches = pair.Matches.GetRange(0, 69) };
            var alias = new Match { A = pair.Matches[0].A, B = [pair.Matches[0].B[0] + 40, pair.Matches[0].B[1]] };
            var proposed = new ImagePair { A = left.Id, B = right.Id, Matches = [pair.Matches[69], alias] };
            var recovered = AlignmentRecoveryFilter.Filter(project, clean, [reduced], [proposed], default);
            Check(recovered[0].Matches.Count == 70 && !recovered[0].Matches.Contains(alias),
                "Independent 3D observations must reject same-epipolar-line aliases while admitting a real added match");
            var depleted = new ReconstructionScene { Points = clean.Points.GetRange(0, 20) };
            Check(!AlignmentRecoveryEvaluator.Evaluate(clean, depleted).Accepted, "Recovery cannot replace a cloud after losing established observations");
            var expanded = new ReconstructionScene { Points = new List<CloudPoint>(clean.Points) };
            expanded.Points.Add(new CloudPoint { ImageA = left.Id, PixelA = pair.Matches[0].A, ImageB = "third", PixelB = [200, 100] });
            Check(AlignmentRecoveryEvaluator.Evaluate(clean, expanded).Accepted, "Recovery that preserves and expands observations should pass");

            var manual = new Match { A = [100, 100], B = [110, 100], Manual = true };
            var failed = new ImagePair { A = left.Id, B = right.Id, Matches = [wrong[0], manual] };
            var retained = ImagePairVerifier.Verify(left, right, failed, default);
            Check(retained.Matches.Count == 1 && ReferenceEquals(retained.Matches[0], manual), "A failed fit must reject automatic matches while retaining explicit manual ties");
            Check(manual.GeometricInlier == null, "Manual ties must not be mislabeled as geometrically verified");
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
