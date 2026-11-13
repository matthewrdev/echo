using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Infrastructure;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Echo.Optimization;

[Export(typeof(IBundleAdjuster)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class BundleAdjuster : IBundleAdjuster
{
    private readonly ICameraGeometry cameraGeometry;
    private readonly IJsonCodec jsonCodec;

    [ImportingConstructor]
    public BundleAdjuster(ICameraGeometry cameraGeometry, IJsonCodec jsonCodec)
    {
        this.cameraGeometry = cameraGeometry;
        this.jsonCodec = jsonCodec;
    }

    public void Refine(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<ImagePair> pairs,
        CancellationToken cancellation,
        Action<ReconstructionScene>? checkpoint = null)
    {
        var cameras = scene.Cameras;
        var photos = cameras.Select(c => project.Images.Single(i => i.Id == c.ImageId)).ToArray();
        var initial = cameras.Select(jsonCodec.Clone).ToArray();
        for (var i = 0; i < cameras.Count; i++)
        {
            cameras[i].FocalPixels = photos[i].FocalPixels;
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
                    var p = cameraGeometry.Project(photos[observation.Camera], cameras[observation.Camera], track.Point);
                    if (p == null)
                    {
                        cost += 100000;
                        continue;
                    }

                    var scale = 1000.0 / photos[observation.Camera].Width;
                    var error = Math.Sqrt(Math.Pow(p[0] - observation.Pixel[0], 2) + Math.Pow(p[1] - observation.Pixel[1], 2)) * scale;
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

        var cost = Cost();
        for (var iteration = 0; iteration < project.Settings.BundleIterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            var h = new double[parameter * parameter];
            var b = new double[parameter];
            var blocks = new List<PointBlock>();
            foreach (var track in tracks)
            {
                cancellation.ThrowIfCancellationRequested();
                var block = new PointBlock();
                blocks.Add(block);
                foreach (var observation in track.Observations)
                {
                    var ci = observation.Camera;
                    var evaluation = Evaluate(photos[ci], cameras[ci], track.Point);
                    if (evaluation == null)
                    {
                        continue;
                    }

                    var scale = 1000.0 / photos[ci].Width;
                    var ru = (observation.Pixel[0] - evaluation.U) * scale;
                    var rv = (observation.Pixel[1] - evaluation.V) * scale;
                    var error = Math.Sqrt(ru * ru + rv * rv);
                    var weight = Math.Min(1, 3 / Math.Max(error, 1e-9));
                    var indices = cameraIndices[ci].Concat([focalIndices[ci]]).ToArray();
                    var ju = evaluation.CameraU.Concat([evaluation.FocalU]).Select(v => v * scale).ToArray();
                    var jv = evaluation.CameraV.Concat([evaluation.FocalV]).Select(v => v * scale).ToArray();
                    var pu = evaluation.PointU.Select(v => v * scale).ToArray();
                    var pv = evaluation.PointV.Select(v => v * scale).ToArray();
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
                        if (!block.Cross.TryGetValue(row, out var cross))
                        {
                            cross = new double[3];
                            block.Cross[row] = cross;
                        }

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

                block.Inverse = Inverse3(block.H);
                foreach (var row in block.Cross)
                {
                    var transformed = Multiply(block.Inverse, row.Value);
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

            var oldCameras = cameras.Select(jsonCodec.Clone).ToArray();
            var oldPoints = tracks.Select(t => t.Point).ToArray();
            for (var i = 0; i < cameras.Count; i++)
            {
                var step = cameraIndices[i].Select(index => index < 0 ? 0 : delta[index]).ToArray();
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
                var rhs = (double[])block.B.Clone();
                foreach (var cross in block.Cross)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        rhs[j] -= cross.Value[j] * delta[cross.Key];
                    }
                }

                var step = new V3(Multiply(block.Inverse, rhs));
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
                    cameras[i].Centre = oldCameras[i].Centre;
                    cameras[i].Rotation = oldCameras[i].Rotation;
                    cameras[i].FocalPixels = oldCameras[i].FocalPixels;
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

            var relative = cameraGeometry.EstimateRelative(photos[a], photos[b], pair.Matches);
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

    private Evaluation? Evaluate(Photo photo, CameraPose camera, V3 point)
    {
        var inverse = cameraGeometry.Transpose(camera.Rotation);
        var p = cameraGeometry.Rotate(inverse, point - new V3(camera.Centre));
        if (p.Z <= .001)
        {
            return null;
        }

        var f = cameraGeometry.Focal(photo, camera);
        double[] u = [f / p.Z, 0, -f * p.X / (p.Z * p.Z)];
        double[] v = [0, f / p.Z, -f * p.Y / (p.Z * p.Z)];
        var pu = new double[3];
        var pv = new double[3];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                pu[i] += u[j] * inverse[j * 3 + i];
                pv[i] += v[j] * inverse[j * 3 + i];
            }
        }

        double[] skew = [0, -p.Z, p.Y, p.Z, 0, -p.X, -p.Y, p.X, 0];
        var cu = new double[6];
        var cv = new double[6];
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

        return new Evaluation
        {
            U = f * p.X / p.Z + photo.Width * .5,
            V = f * p.Y / p.Z + photo.Height * .5,
            PointU = pu,
            PointV = pv,
            CameraU = cu,
            CameraV = cv,
            FocalU = f * p.X / p.Z,
            FocalV = f * p.Y / p.Z
        };
    }

    private double Error(V3 point, Observation o, Photo[] photos, List<CameraPose> cameras)
    {
        var p = cameraGeometry.Project(photos[o.Camera], cameras[o.Camera], point);
        if (p == null)
        {
            return double.PositiveInfinity;
        }

        return Math.Sqrt(Math.Pow(p[0] - o.Pixel[0], 2) + Math.Pow(p[1] - o.Pixel[1], 2)) * 1000 / photos[o.Camera].Width;
    }

    private double MedianError(List<Track> tracks, Photo[] photos, List<CameraPose> cameras)
    {
        var values = tracks.SelectMany(t => t.Observations.Select(o => Error(t.Point, o, photos, cameras))).Order().ToArray();
        return values.Length == 0 ? 0 : values[values.Length / 2];
    }

    private void UpdateScene(ReconstructionScene scene, List<Track> tracks, Photo[] photos)
    {
        scene.Points.Clear();
        foreach (var track in tracks)
        {
            if (track.Observations.Count < 2)
            {
                continue;
            }

            var a = track.Observations[0];
            foreach (var b in track.Observations.Skip(1))
            {
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

    private double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private double[] Multiply(double[] matrix, double[] vector) => [matrix[0] * vector[0] + matrix[1] * vector[1] + matrix[2] * vector[2], matrix[3] * vector[0] + matrix[4] * vector[1] + matrix[5] * vector[2], matrix[6] * vector[0] + matrix[7] * vector[1] + matrix[8] * vector[2]];

    private double[] Inverse3(double[] m)
    {
        var determinant = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(determinant) < 1e-20)
        {
            throw new EchoException("Degenerate point geometry.", 5);
        }

        return new[]
        {
            m[4] * m[8] - m[5] * m[7],
            m[2] * m[7] - m[1] * m[8],
            m[1] * m[5] - m[2] * m[4],
            m[5] * m[6] - m[3] * m[8],
            m[0] * m[8] - m[2] * m[6],
            m[2] * m[3] - m[0] * m[5],
            m[3] * m[7] - m[4] * m[6],
            m[1] * m[6] - m[0] * m[7],
            m[0] * m[4] - m[1] * m[3]
        }.Select(v => v / determinant).ToArray();
    }

    private double[] Exponential(V3 w)
    {
        var angle = w.Length;
        if (angle < 1e-12)
        {
            return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        }

        var n = w / angle;
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var t = 1 - c;
        return [c + n.X * n.X * t, n.X * n.Y * t - n.Z * s, n.X * n.Z * t + n.Y * s, n.Y * n.X * t + n.Z * s, c + n.Y * n.Y * t, n.Y * n.Z * t - n.X * s, n.Z * n.X * t - n.Y * s, n.Z * n.Y * t + n.X * s, c + n.Z * n.Z * t];
    }
}
