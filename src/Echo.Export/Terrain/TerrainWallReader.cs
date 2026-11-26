using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Export.Terrain;

internal sealed class TerrainWallReader
{
    public TerrainWall Read(TerrainWallInput input, TerrainGrid grid, double maxSeamError, CancellationToken cancellation)
    {
        var label = input.Name ?? Path.GetFileName(input.Directory.TrimEnd(Path.DirectorySeparatorChar));
        using var reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input.Directory, "cliff.geo.json")));
        if (!reference.RootElement.TryGetProperty("origin", out var entry) || entry.ValueKind != JsonValueKind.Object)
        {
            throw new EchoException($"{label} has no geographic alignment.", 2);
        }

        var origin = JsonSerializer.Deserialize<GeoPosition>(entry,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var offset = input.HeightOffsetMetres;
        var compatible = origin.AltitudeDatum.Equals(grid.Origin.AltitudeDatum, StringComparison.OrdinalIgnoreCase)
            && !origin.AltitudeDatum.Equals("unknown", StringComparison.OrdinalIgnoreCase);
        if (!origin.Altitude.HasValue || (!compatible && offset == null) || (offset is double shift && !double.IsFinite(shift)))
        {
            throw new EchoException($"{label} needs a geographic height and an explicit height offset when vertical datums differ.", 2);
        }

        var data = ReadMesh(Path.Combine(input.Directory, "cliff.glb"));
        var count = data.Positions.Length / 3;
        var positions = new V3[count];
        var pixels = new double[count][];
        var welded = new int[count];
        var representatives = new Dictionary<V3, int>();
        var left = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.PositiveInfinity;
        var bottom = double.NegativeInfinity;
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var source = new V3(data.Positions[3 * index], data.Positions[3 * index + 1], data.Positions[3 * index + 2]);
            representatives.TryAdd(source, index);
            welded[index] = representatives[source];
            var geographic = GeographicCoordinates.FromLocal(source, origin);
            geographic.Altitude += offset ?? 0;
            var column = grid.Column(geographic.Longitude);
            var row = grid.Row(geographic.Latitude);
            var altitude = geographic.Altitude!.Value;
            if (!double.IsFinite(column) || !double.IsFinite(row) || !double.IsFinite(altitude))
            {
                throw new EchoException($"{label} produced invalid geographic coordinates.", 2);
            }

            left = Math.Min(column, left);
            right = Math.Max(column, right);
            top = Math.Min(row, top);
            bottom = Math.Max(row, bottom);
            positions[index] = CameraGeometry.ToLocal(geographic, grid.Origin);
            pixels[index] = [column, row, altitude];
        }

        if (left < 2 || top < 2 || right >= grid.Columns - 3 || bottom >= grid.Rows - 3)
        {
            throw new EchoException($"{label} needs more terrain on all sides to place its seam.", 2);
        }

        var window = new TerrainGridWindow((int)Math.Floor(left) - 1, (int)Math.Floor(top) - 1,
            (int)Math.Ceiling(right) + 1, (int)Math.Ceiling(bottom) + 1);
        var boundary = Boundary(data.Indices, welded, label, cancellation);
        var area = 0d;
        var squareError = 0d;
        var maximum = 0d;
        var samples = 0;
        for (var index = 0; index < boundary.Length; index++)
        {
            var first = pixels[boundary[index]];
            var next = pixels[boundary[(index + 1) % boundary.Length]];
            area += first[0] * next[1] - first[1] * next[0];
            var distance = Math.Max(Math.Abs(first[0] - next[0]), Math.Abs(first[1] - next[1]));
            var steps = Math.Max(1, (int)Math.Ceiling(distance));
            for (var step = 0; step < steps; step++)
            {
                var t = (double)step / steps;
                var column = first[0] + t * (next[0] - first[0]);
                var row = first[1] + t * (next[1] - first[1]);
                var height = first[2] + t * (next[2] - first[2]);
                var error = Math.Abs(height - grid.Elevation(column, row));
                maximum = Math.Max(maximum, error);
                squareError += error * error;
                samples++;
            }
        }

        if (maximum > maxSeamError)
        {
            throw new EchoException(FormattableString.Invariant(
                $"{label} has a maximum terrain seam error of {maximum:F2} m, above the requested {maxSeamError:F2} m. Review the alignment or crop."), 2);
        }

        if (Math.Abs(area) < 1e-8)
        {
            throw new EchoException($"{label} has no usable horizontal footprint.", 2);
        }

        if (area < 0)
        {
            Array.Reverse(boundary);
        }
        else
        {
            for (var index = 0; index < data.Indices.Length; index += 3)
            {
                var second = data.Indices[index + 1];
                data.Indices[index + 1] = data.Indices[index + 2];
                data.Indices[index + 2] = second;
            }
        }

        return new TerrainWall(label, positions, data.Indices, data.Uvs, pixels, boundary, window, data.ImageBytes().ToArray(),
            Math.Sqrt(squareError / samples), maximum);
    }

    private static int[] Boundary(uint[] faces, int[] welded, string label, CancellationToken cancellation)
    {
        var edges = new Dictionary<ulong, TerrainWallEdge>();
        var parent = Enumerable.Range(0, welded.Length).ToArray();
        var used = new HashSet<int>();
        for (var face = 0; face < faces.Length; face += 3)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = welded[(int)faces[face]];
            var b = welded[(int)faces[face + 1]];
            var c = welded[(int)faces[face + 2]];
            if (a == b || b == c || c == a)
            {
                throw new EchoException($"{label} contains collapsed faces.", 2);
            }

            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
            parent[Root(b)] = Root(a);
            parent[Root(c)] = Root(a);
            used.Add(a);
            used.Add(b);
            used.Add(c);
        }

        var component = Root(used.First());
        if (used.Any(index => Root(index) != component))
        {
            throw new EchoException($"{label} contains disconnected components. Crop and export one connected surface.", 2);
        }

        var outgoing = new Dictionary<int, int>();
        var incoming = new HashSet<int>();
        foreach (var edge in edges.Values)
        {
            if (edge.References == 1 && (!outgoing.TryAdd(edge.From, edge.To) || !incoming.Add(edge.To)))
            {
                throw new EchoException($"{label} has a branching boundary. Export an open crop with one rim.", 2);
            }
        }

        if (outgoing.Count < 3)
        {
            throw new EchoException($"{label} has no open rim. Export a cropped surface with hull closure off.", 2);
        }

        var ordered = new List<int>(outgoing.Count);
        var start = outgoing.Keys.Min();
        var current = start;
        var boundaryCount = outgoing.Count;
        do
        {
            ordered.Add(current);
            if (!outgoing.Remove(current, out var following))
            {
                throw new EchoException($"{label} has an incomplete boundary.", 2);
            }

            current = following;
        }
        while (current != start);

        if (ordered.Count != boundaryCount)
        {
            throw new EchoException($"{label} has multiple boundaries. Export a crop with one open rim.", 2);
        }

        return ordered.ToArray();

        int Root(int index)
        {
            while (index != parent[index])
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        void AddEdge(int from, int to)
        {
            var key = ((ulong)(uint)Math.Min(from, to) << 32) | (uint)Math.Max(from, to);
            if (!edges.TryGetValue(key, out var previous))
            {
                edges.Add(key, new TerrainWallEdge(from, to, 1));
                return;
            }

            if (previous.References != 1 || previous.From != to)
            {
                throw new EchoException($"{label} has non-manifold or inconsistently oriented edges.", 2);
            }

            edges[key] = previous with { References = 2 };
        }
    }

    private static DracoGlbSource ReadMesh(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != reader.BaseStream.Length)
        {
            throw new EchoException("The saved wall is not a GLB 2.0 asset.", 2);
        }

        var json = Chunk(0x4e4f534a);
        var binary = Chunk(0x004e4942);
        return new DracoGlbSource(JsonNode.Parse(json)!.AsObject(), binary);

        byte[] Chunk(uint type)
        {
            var size = reader.ReadInt32();
            if (size < 0 || reader.ReadUInt32() != type || size > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new EchoException("The saved wall has an invalid GLB chunk.", 2);
            }

            return reader.ReadBytes(size);
        }
    }
}
