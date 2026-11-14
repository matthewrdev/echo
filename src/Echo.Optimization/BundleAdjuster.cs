using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Optimization;

[Export(typeof(IBundleAdjuster)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class BundleAdjuster : IBundleAdjuster
{
    private readonly ICameraGeometry cameraGeometry;
    private readonly IMatrix3Inverter matrixInverter;

    [ImportingConstructor]
    public BundleAdjuster(ICameraGeometry cameraGeometry, IMatrix3Inverter matrixInverter)
    {
        this.cameraGeometry = cameraGeometry;
        this.matrixInverter = matrixInverter;
    }

    public void Refine(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null)
    {
        var cameras = scene.Cameras;
        var imageLookup = new Dictionary<string, Photo>(project.Images.Count, StringComparer.Ordinal);
        foreach (var photo in project.Images)
        {
            imageLookup.Add(photo.Id, photo);
        }
        var photos = new Photo[cameras.Count];
        var initial = new CameraState[cameras.Count];
        var oldCameras = new CameraState[cameras.Count];
        for (var i = 0; i < cameras.Count; i++)
        {
            photos[i] = imageLookup[cameras[i].ImageId];
            initial[i] = new CameraState(cameras[i]);
            var position = photos[i].Gps;
            foreach (var hint in project.Hints)
            {
                var applies = hint.Enabled && hint.Type == "camera" && hint.ImageId == photos[i].Id;
                if (applies)
                {
                    position = hint.Position ?? position;
                }
            }
            if (scene.Origin != null && position != null)
            {
                initial[i] = initial[i] with
                {
                    Centre = cameraGeometry.ToLocal(position, scene.Origin).Array
                };
            }
        }
        for (var i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].FocalPixels <= 0)
            {
                cameras[i].FocalPixels = photos[i].FocalPixels;
            }
        }

        var tracks = BuildTracks(photos, cameras, pairs, cancellation);
        if (tracks.Count < 20)
        {
            throw new EchoException($"Only {tracks.Count} consistent multi-image tracks. More overlap or stronger features are needed for joint camera refinement.", 5);
        }

        var report = new BundleReport
        {
            InitialMedianError = MedianError(tracks, photos, cameras)
        };
        scene.Bundle = report;
        var parameter = 0;
        var cameraIndices = new int[cameras.Count][];
        for (var i = 0; i < cameras.Count; i++)
        {
            cameraIndices[i] = new int[6];
            for (var j = 0; j < 6; j++)
            {
                cameraIndices[i][j] = i == 0 || scene.Origin == null && i == 1 && j < 3 ? -1 : parameter++;
            }
        }

        var groups = new Dictionary<string, int>();
        var focalIndices = new int[cameras.Count];
        for (var i = 0; i < cameras.Count; i++)
        {
            var key = $"{photos[i].Camera}:{photos[i].Width}:{photos[i].Height}:{photos[i].FocalPixels:G8}";
            if (!groups.TryGetValue(key, out var index))
            {
                index = parameter++;
                groups[key] = index;
            }

            focalIndices[i] = index;
        }

        var lambda = .001;
        var priorWeight = 1.0 / 100;
        double Cost()
        {
            double cost = 0;
            foreach (var track in tracks)
            {
                foreach (var observation in track.Observations)
                {
                    var p = cameraGeometry.ProjectPoint(photos[observation.Camera], cameras[observation.Camera], track.Point);
                    if (p == null)
                    {
                        cost += 100000;
                        continue;
                    }

                    var scale = 1000.0 / photos[observation.Camera].Width;
                    var error = Math.Sqrt(Math.Pow(p.Value.X - observation.Pixel[0], 2) + Math.Pow(p.Value.Y - observation.Pixel[1], 2)) * scale;
                    cost += error < 3 ? error * error : 6 * error - 9;
                }
            }

            if (scene.Origin != null)
            {
                for (var i = 1; i < cameras.Count; i++)
                {
                    if (photos[i].Gps != null)
                    {
                        cost += (new V3(cameras[i].Centre) - new V3(initial[i].Centre)).Length * (new V3(cameras[i].Centre) - new V3(initial[i].Centre)).Length * priorWeight;
                    }
                }
            }

            foreach (var group in groups.Values)
            {
                var i = Array.IndexOf(focalIndices, group);
                var log = Math.Log(cameras[i].FocalPixels / photos[i].FocalPixels);
                cost += log * log * 100;
            }

            return cost;
        }

        var evaluation = new Evaluation();
        Span<int> indices = stackalloc int[7];
        Span<double> ju = stackalloc double[7];
        Span<double> jv = stackalloc double[7];
        Span<double> pu = stackalloc double[3];
        Span<double> pv = stackalloc double[3];
        Span<double> transformed = stackalloc double[3];
        var cost = Cost();
        var oldPoints = new V3[tracks.Count];
        Span<double> cameraStep = stackalloc double[6];
        Span<double> pointRightHandSide = stackalloc double[3];
        Span<double> pointStep = stackalloc double[3];
        var h = new double[checked(parameter * parameter)];
        var b = new double[parameter];
        var blocks = new PointBlock[tracks.Count];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new PointBlock();
        }

        for (var iteration = 0; iteration < project.Settings.BundleIterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            h.AsSpan().Clear();
            b.AsSpan().Clear();
            for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var track = tracks[trackIndex];
                var block = blocks[trackIndex];
                block.Reset();
                foreach (var observation in track.Observations)
                {
                    var ci = observation.Camera;
                    if (!Evaluate(photos[ci], cameras[ci], track.Point, evaluation))
                    {
                        continue;
                    }

                    var scale = 1000.0 / photos[ci].Width;
                    var ru = (observation.Pixel[0] - evaluation.U) * scale;
                    var rv = (observation.Pixel[1] - evaluation.V) * scale;
                    var error = Math.Sqrt(ru * ru + rv * rv);
                    var weight = Math.Min(1, 3 / Math.Max(error, 1e-9));
                    cameraIndices[ci].AsSpan().CopyTo(indices);
                    indices[6] = focalIndices[ci];
                    for (var j = 0; j < 6; j++)
                    {
                        ju[j] = evaluation.CameraU[j] * scale;
                        jv[j] = evaluation.CameraV[j] * scale;
                    }

                    ju[6] = evaluation.FocalU * scale;
                    jv[6] = evaluation.FocalV * scale;
                    for (var j = 0; j < 3; j++)
                    {
                        pu[j] = evaluation.PointU[j] * scale;
                        pv[j] = evaluation.PointV[j] * scale;
                    }

                    for (var j = 0; j < 3; j++)
                    {
                        block.B[j] += weight * (pu[j] * ru + pv[j] * rv);
                        for (var k = 0; k < 3; k++)
                        {
                            block.H[j * 3 + k] += weight * (pu[j] * pu[k] + pv[j] * pv[k]);
                        }
                    }

                    for (var j = 0; j < indices.Length; j++)
                    {
                        var row = indices[j];
                        if (row < 0)
                        {
                            continue;
                        }

                        b[row] += weight * (ju[j] * ru + jv[j] * rv);
                        var cross = block.GetCross(row);

                        for (var k = 0; k < 3; k++)
                        {
                            cross[k] += weight * (ju[j] * pu[k] + jv[j] * pv[k]);
                        }

                        for (var k = 0; k < indices.Length; k++)
                        {
                            if (indices[k] >= 0)
                            {
                                h[row * parameter + indices[k]] += weight * (ju[j] * ju[k] + jv[j] * jv[k]);
                            }
                        }
                    }
                }
            }

            if (scene.Origin != null)
            {
                for (var i = 1; i < cameras.Count; i++)
                {
                    if (photos[i].Gps != null)
                    {
                        for (var j = 0; j < 3; j++)
                        {
                            var index = cameraIndices[i][j];
                            h[index * parameter + index] += priorWeight;
                            b[index] += (initial[i].Centre[j] - cameras[i].Centre[j]) * priorWeight;
                        }
                    }
                }
            }

            foreach (var group in groups.Values)
            {
                var ci = Array.IndexOf(focalIndices, group);
                h[group * parameter + group] += 100;
                b[group] -= Math.Log(cameras[ci].FocalPixels / photos[ci].FocalPixels) * 100;
            }

            for (var i = 0; i < parameter; i++)
            {
                h[i * parameter + i] += lambda * Math.Max(1e-5, h[i * parameter + i]);
            }

            foreach (var block in blocks)
            {
                for (var i = 0; i < 3; i++)
                {
                    block.H[i * 3 + i] += lambda * Math.Max(1e-5, block.H[i * 3 + i]);
                }

                Inverse3(block.H, block.Inverse);
                foreach (var row in block.Cross)
                {
                    Multiply(block.Inverse, row.Value, transformed);
                    b[row.Key] -= Dot(transformed, block.B);
                    foreach (var column in block.Cross)
                    {
                        h[row.Key * parameter + column.Key] -= Dot(transformed, column.Value);
                    }
                }
            }

            var matrix = DenseMatrix.Create(parameter, parameter, (r, c) => h[r * parameter + c]);
            Vector<double> delta;
            try
            {
                delta = matrix.Cholesky().Solve(DenseVector.OfArray(b));
            }
            catch (ArgumentException)
            {
                lambda *= 10;
                continue;
            }

            if (!delta.All(double.IsFinite))
            {
                lambda *= 10;
                continue;
            }

            for (var i = 0; i < cameras.Count; i++)
            {
                oldCameras[i] = new CameraState(cameras[i]);
            }
            for (var i = 0; i < tracks.Count; i++)
            {
                oldPoints[i] = tracks[i].Point;
            }
            for (var i = 0; i < cameras.Count; i++)
            {
                var step = cameraStep;
                for (var axis = 0; axis < 6; axis++)
                {
                    var index = cameraIndices[i][axis];
                    step[axis] = index < 0 ? 0 : delta[index];
                }
                var translation = new V3(step[0], step[1], step[2]);
                if (translation.Length > 3)
                {
                    translation = translation.Unit * 3;
                }

                cameras[i].Centre = (new V3(cameras[i].Centre) + translation).Array;
                var rotation = new V3(step[3], step[4], step[5]);
                if (rotation.Length > .07)
                {
                    rotation = rotation.Unit * .07;
                }

                cameras[i].Rotation = cameraGeometry.Multiply(cameras[i].Rotation, Exponential(rotation));
                cameras[i].FocalPixels = Math.Clamp(cameras[i].FocalPixels * Math.Exp(Math.Clamp(delta[focalIndices[i]], -.025, .025)), photos[i].FocalPixels * .65, photos[i].FocalPixels * 1.5);
            }

            for (var i = 0; i < tracks.Count; i++)
            {
                var block = blocks[i];
                var rhs = pointRightHandSide;
                block.B.AsSpan().CopyTo(rhs);
                foreach (var cross in block.Cross)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        rhs[j] -= cross.Value[j] * delta[cross.Key];
                    }
                }

                Multiply(block.Inverse, rhs, pointStep);
                var step = new V3(pointStep[0], pointStep[1], pointStep[2]);
                if (step.Length > 5)
                {
                    step = step.Unit * 5;
                }

                tracks[i].Point += step;
            }

            var nextCost = Cost();
            if (double.IsFinite(nextCost) && nextCost < cost)
            {
                var improvement = cost - nextCost;
                cost = nextCost;
                lambda = Math.Max(1e-7, lambda * .4);
                report.AcceptedIterations++;
                if (iteration % 5 == 4)
                {
                    UpdateScene(scene, tracks, photos);
                    checkpoint?.Invoke(scene);
                }

                if (improvement < .000001 * Math.Max(1, cost))
                {
                    break;
                }
            }
            else
            {
                for (var i = 0; i < cameras.Count; i++)
                {
                    oldCameras[i].Restore(cameras[i]);
                }

                for (var i = 0; i < tracks.Count; i++)
                {
                    tracks[i].Point = oldPoints[i];
                }

                lambda *= 10;
            }
        }

        report.FinalUntrimmedMedianError = MedianError(tracks, photos, cameras);
        foreach (var track in tracks)
        {
            track.Observations = track.Observations.Where(o => Error(track.Point, o, photos, cameras) < 4).ToList();
        }

        tracks = tracks.Where(t => t.Observations.Count >= 3).ToList();
        if (tracks.Count < 20)
        {
            throw new EchoException("Joint refinement left too few consistent tracks. Inspect matching and supply stronger overlap.", 5);
        }

        report.Tracks = tracks.Count;
        report.Observations = tracks.Sum(t => t.Observations.Count);
        report.FinalMedianError = MedianError(tracks, photos, cameras);
        report.MaximumCameraMovementMetres = cameras.Select((c, i) => (new V3(c.Centre) - new V3(initial[i].Centre)).Length).Max();
        foreach (var camera in cameras)
        {
            camera.Source += "; joint bundle adjustment";
        }

        UpdateScene(scene, tracks, photos);
        checkpoint?.Invoke(scene);
    }

    private List<Track> BuildTracks(Photo[] photos, List<CameraPose> cameras, IReadOnlyList<ImagePair> pairs, CancellationToken cancellation)
    {
        var cameraLookup = cameras.Select((c, i) => new
        {
            c.ImageId,
            Index = i
        }).ToDictionary(c => c.ImageId, c => c.Index);
        var links = new List<Link>();
        foreach (var pair in pairs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!cameraLookup.TryGetValue(pair.A, out var a) || !cameraLookup.TryGetValue(pair.B, out var b))
            {
                continue;
            }

            var relative = FundamentalMatrixEstimator.Estimate(pair.Matches, Math.Max(photos[a].Width, photos[b].Width) / 1000.0, cancellation);
            var matches = relative?.Inliers ?? pair.Matches.Where(m => m.Manual).ToList();
            foreach (var match in matches)
            {
                links.Add(new Link
                {
                    A = new Observation
                    {
                        Camera = a,
                        Pixel = match.A
                    },
                    B = new Observation
                    {
                        Camera = b,
                        Pixel = match.B
                    },
                    Score = match.Manual ? -1 : match.Score
                });
            }
        }

        var nodes = new List<Observation>();
        var parents = new List<int>();
        var sets = new List<HashSet<int>>();
        var lookup = new Dictionary<ObservationKey, int>();
        int Add(Observation observation)
        {
            var key = new ObservationKey(observation.Camera, (long)Math.Round(observation.Pixel[0] * 10), (long)Math.Round(observation.Pixel[1] * 10));
            if (lookup.TryGetValue(key, out var index))
            {
                return index;
            }

            index = nodes.Count;
            nodes.Add(observation);
            parents.Add(index);
            sets.Add([observation.Camera]);
            lookup[key] = index;
            return index;
        }

        int Root(int i)
        {
            while (parents[i] != i)
            {
                parents[i] = parents[parents[i]];
                i = parents[i];
            }

            return i;
        }

        foreach (var link in links.OrderBy(l => l.Score))
        {
            var a = Root(Add(link.A));
            var b = Root(Add(link.B));
            if (a == b || sets[a].Overlaps(sets[b]))
            {
                continue;
            }

            if (sets[a].Count < sets[b].Count)
            {
                var swap = a;
                a = b;
                b = swap;
            }

            parents[b] = a;
            sets[a].UnionWith(sets[b]);
        }

        var tracks = new List<Track>();
        foreach (var group in Enumerable.Range(0, nodes.Count).GroupBy(Root))
        {
            var observations = group.Select(i => nodes[i]).ToList();
            if (observations.Count < 3)
            {
                continue;
            }

            var h = new double[9];
            var rhs = new double[3];
            foreach (var o in observations)
            {
                var camera = cameras[o.Camera];
                var ray = cameraGeometry.Rotate(camera.Rotation, cameraGeometry.Ray(photos[o.Camera], o.Pixel, camera)).Unit.Array;
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        var value = (i == j ? 1 : 0) - ray[i] * ray[j];
                        h[i * 3 + j] += value;
                        rhs[i] += value * camera.Centre[j];
                    }
                }
            }

            V3 point;
            try
            {
                point = new V3(Multiply(Inverse3(h), rhs));
            }
            catch (EchoException)
            {
                continue;
            }

            if (!point.Array.All(double.IsFinite))
            {
                continue;
            }

            var valid = observations.Where(o => Error(point, o, photos, cameras) < 30).ToList();
            if (valid.Count >= 3)
            {
                tracks.Add(new Track
                {
                    Point = point,
                    Observations = valid
                });
            }
        }

        return tracks;
    }

    private bool Evaluate(Photo photo, CameraPose camera, V3 point, Evaluation evaluation)
    {
        var rotation = camera.Rotation;
        ReadOnlySpan<double> inverse = stackalloc double[9]
        {
            rotation[0], rotation[3], rotation[6],
            rotation[1], rotation[4], rotation[7],
            rotation[2], rotation[5], rotation[8]
        };
        var delta = point - new V3(camera.Centre);
        var p = new V3(
            inverse[0] * delta.X + inverse[1] * delta.Y + inverse[2] * delta.Z,
            inverse[3] * delta.X + inverse[4] * delta.Y + inverse[5] * delta.Z,
            inverse[6] * delta.X + inverse[7] * delta.Y + inverse[8] * delta.Z);
        if (p.Z <= .001)
        {
            return false;
        }

        var f = cameraGeometry.Focal(photo, camera);
        ReadOnlySpan<double> u = stackalloc double[3] { f / p.Z, 0, -f * p.X / (p.Z * p.Z) };
        ReadOnlySpan<double> v = stackalloc double[3] { 0, f / p.Z, -f * p.Y / (p.Z * p.Z) };
        var pu = evaluation.PointU.AsSpan();
        var pv = evaluation.PointV.AsSpan();
        pu.Clear();
        pv.Clear();
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                pu[i] += u[j] * inverse[j * 3 + i];
                pv[i] += v[j] * inverse[j * 3 + i];
            }
        }

        ReadOnlySpan<double> skew = stackalloc double[9] { 0, -p.Z, p.Y, p.Z, 0, -p.X, -p.Y, p.X, 0 };
        var cu = evaluation.CameraU.AsSpan();
        var cv = evaluation.CameraV.AsSpan();
        cu.Clear();
        cv.Clear();
        for (var i = 0; i < 3; i++)
        {
            cu[i] = -pu[i];
            cv[i] = -pv[i];
            for (var j = 0; j < 3; j++)
            {
                cu[i + 3] += u[j] * skew[j * 3 + i];
                cv[i + 3] += v[j] * skew[j * 3 + i];
            }
        }

        evaluation.U = f * p.X / p.Z + photo.Width * .5;
        evaluation.V = f * p.Y / p.Z + photo.Height * .5;
        evaluation.FocalU = f * p.X / p.Z;
        evaluation.FocalV = f * p.Y / p.Z;
        return true;
    }

    private double Error(V3 point, Observation observation, Photo[] photos, List<CameraPose> cameras)
    {
        var p = cameraGeometry.ProjectPoint(photos[observation.Camera], cameras[observation.Camera], point);
        if (p == null)
        {
            return double.PositiveInfinity;
        }

        return Math.Sqrt(Math.Pow(p.Value.X - observation.Pixel[0], 2) + Math.Pow(p.Value.Y - observation.Pixel[1], 2)) * 1000 / photos[observation.Camera].Width;
    }

    private double MedianError(List<Track> tracks, Photo[] photos, List<CameraPose> cameras)
    {
        var values = tracks.SelectMany(t => t.Observations.Select(o => Error(t.Point, o, photos, cameras))).Order().ToArray();
        return values.Length == 0 ? 0 : values[values.Length / 2];
    }

    private void UpdateScene(ReconstructionScene scene, List<Track> tracks, Photo[] photos)
    {
        scene.Points.Clear();
        var visible = new List<Observation>();
        foreach (var track in tracks)
        {
            visible.Clear();
            foreach (var observation in track.Observations)
            {
                if (Error(track.Point, observation, photos, scene.Cameras) < 4)
                {
                    visible.Add(observation);
                }
            }
            if (visible.Count < 3)
            {
                continue;
            }

            var a = visible[0];
            for (var index = 1; index < visible.Count; index++)
            {
                var b = visible[index];
                scene.Points.Add(new CloudPoint
                {
                    Position = track.Point.Array,
                    ImageA = photos[a.Camera].Id,
                    ImageB = photos[b.Camera].Id,
                    PixelA = a.Pixel,
                    PixelB = b.Pixel,
                    Error = Error(track.Point, b, photos, scene.Cameras) * photos[b.Camera].Width / 1000
                });
            }
        }
    }

    private double Dot(ReadOnlySpan<double> leftVector, ReadOnlySpan<double> rightVector) => leftVector[0] * rightVector[0] + leftVector[1] * rightVector[1] + leftVector[2] * rightVector[2];

    private void Multiply(ReadOnlySpan<double> matrix, ReadOnlySpan<double> vector, Span<double> result)
    {
        for (var row = 0; row < 3; row++)
        {
            result[row] = matrix[row * 3] * vector[0] + matrix[row * 3 + 1] * vector[1] + matrix[row * 3 + 2] * vector[2];
        }
    }

    private double[] Multiply(double[] matrix, double[] vector) => [matrix[0] * vector[0] + matrix[1] * vector[1] + matrix[2] * vector[2], matrix[3] * vector[0] + matrix[4] * vector[1] + matrix[5] * vector[2], matrix[6] * vector[0] + matrix[7] * vector[1] + matrix[8] * vector[2]];

    private double[] Inverse3(double[] matrix)
    {
        var inverse = new double[9];
        Inverse3(matrix, inverse);
        return inverse;
    }

    private void Inverse3(ReadOnlySpan<double> matrix, Span<double> inverse)
    {
        matrixInverter.Invert(matrix, inverse);
    }

    private double[] Exponential(V3 rotationVector)
    {
        var angle = rotationVector.Length;
        if (angle < 1e-12)
        {
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        }

        var n = rotationVector / angle;
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var t = 1 - c;
        return [c + n.X * n.X * t, n.X * n.Y * t - n.Z * s, n.X * n.Z * t + n.Y * s, n.Y * n.X * t + n.Z * s, c + n.Y * n.Y * t, n.Y * n.Z * t - n.X * s, n.Z * n.X * t - n.Y * s, n.Z * n.Y * t + n.X * s, c + n.Z * n.Z * t];
    }
}
