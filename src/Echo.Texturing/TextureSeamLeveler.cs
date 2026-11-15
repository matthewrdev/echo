using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureSeamLeveler)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureSeamLeveler : ITextureSeamLeveler
{
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public TextureSeamLeveler(IRasterImageService images)
    {
        this.images = images;
    }

    public TextureSeamCorrections Compute(IProjectStore store, ProjectData project, ReconstructionScene scene,
        Surface mesh, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var imageIds = mesh.SourceImages.Distinct().Order(StringComparer.Ordinal).ToArray();
        if (imageIds.Length <= 1)
        {
            return new TextureSeamCorrections();
        }
        var imageIndices = imageIds.Select((id, index) => new KeyValuePair<string, int>(id, index)).ToDictionary();
        var topology = SurfaceTopologyBuilder.Build(mesh, cancellation);
        var corners = new int[mesh.Faces.Count * 3];
        var lookup = new Dictionary<TextureSeamKey, int>();
        var nodes = new List<TextureSeamNode>();
        for (var face = 0; face < mesh.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            var image = imageIndices[mesh.SourceImages[face]];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = topology.WeldedVertices[mesh.Faces[face][corner]];
                var key = new TextureSeamKey(vertex, image);
                if (!lookup.TryGetValue(key, out var node))
                {
                    node = nodes.Count;
                    lookup.Add(key, node);
                    nodes.Add(new TextureSeamNode { Image = image, Vertex = vertex });
                }
                corners[face * 3 + corner] = node;
            }
            for (var corner = 0; corner < 3; corner++)
            {
                var a = corners[face * 3 + corner];
                var b = corners[face * 3 + (corner + 1) % 3];
                if (!nodes[a].Neighbours.Contains(b))
                {
                    nodes[a].Neighbours.Add(b);
                    nodes[b].Neighbours.Add(a);
                }
            }
        }

        var samples = imageIds.Select(_ => new List<TextureSeamSample>()).ToArray();
        var seamEdges = 0;
        for (var face = 0; face < mesh.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var edge = 0; edge < 3; edge++)
            {
                var other = topology.Neighbours[face * 3 + edge];
                if (other <= face || mesh.SourceImages[face] == mesh.SourceImages[other])
                {
                    continue;
                }
                if (string.IsNullOrEmpty(mesh.SourceImages[face]) || string.IsNullOrEmpty(mesh.SourceImages[other]))
                {
                    continue;
                }
                seamEdges++;
                for (var end = 0; end < 2; end++)
                {
                    var corner = (edge + end) % 3;
                    var a = new V3(mesh.Vertices[mesh.Faces[face][corner]]);
                    var b = new V3(mesh.Vertices[mesh.Faces[face][(edge + 1 - end) % 3]]);
                    var first = corners[face * 3 + corner];
                    var second = lookup[new TextureSeamKey(nodes[first].Vertex, imageIndices[mesh.SourceImages[other]])];
                    for (var sample = 0; sample < 3; sample++)
                    {
                        var point = a + (b - a) * (sample * .25);
                        var weight = 3 - sample;
                        samples[nodes[first].Image].Add(new TextureSeamSample(first, point, weight));
                        samples[nodes[second].Image].Add(new TextureSeamSample(second, point, weight));
                    }
                }
            }
        }
        if (seamEdges == 0)
        {
            return new TextureSeamCorrections();
        }
        ReadColours(store, project, scene, imageIds, samples, nodes, cancellation);
        var links = new List<TextureSeamLink>();
        foreach (var group in Enumerable.Range(0, nodes.Count).Where(index => nodes[index].Weight > 0).GroupBy(index => nodes[index].Vertex))
        {
            var boundary = group.ToArray();
            for (var first = 0; first < boundary.Length; first++)
            {
                for (var second = first + 1; second < boundary.Length; second++)
                {
                    links.Add(new TextureSeamLink(boundary[first], boundary[second]));
                }
            }
        }
        var global = GlobalOffsets(nodes, links, imageIds.Length, cancellation);
        var local = LocalOffsets(nodes, global, cancellation);
        var offsets = new V3[corners.Length];
        for (var corner = 0; corner < corners.Length; corner++)
        {
            var node = corners[corner];
            offsets[corner] = global[nodes[node].Image] + local[node];
        }
        var before = 0.0;
        var after = 0.0;
        foreach (var link in links)
        {
            var a = nodes[link.First];
            var b = nodes[link.Second];
            before += (a.Colour - b.Colour).Length / Math.Sqrt(3);
            after += (a.Colour + global[a.Image] + local[link.First] - b.Colour - global[b.Image] - local[link.Second]).Length / Math.Sqrt(3);
        }
        return new TextureSeamCorrections
        {
            CornerOffsets = offsets,
            SeamEdges = seamEdges,
            MeanDifferenceBefore = links.Count == 0 ? 0 : before / links.Count,
            MeanDifferenceAfter = links.Count == 0 ? 0 : after / links.Count
        };
    }

    private void ReadColours(IProjectStore store, ProjectData project, ReconstructionScene scene, string[] imageIds,
        List<TextureSeamSample>[] samples, List<TextureSeamNode> nodes, CancellationToken cancellation)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        for (var image = 0; image < imageIds.Length; image++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (samples[image].Count == 0)
            {
                continue;
            }
            var photo = photos[imageIds[image]];
            var projection = new CameraProjector(photo, cameras[photo.Id]);
            using var bitmap = SKBitmap.Decode(store.PathFor(photo.Source));
            var pixels = images.BorrowPixels(bitmap, out var swap);
            var skyGuard = project.Settings.TextureSuppressSky
                ? TextureSkyGuardBuilder.Build(pixels, bitmap.Width, bitmap.Height, swap, project.Settings.TextureSkyMarginPixels, cancellation)
                : null;
            foreach (var sample in samples[image])
            {
                var pixel = projection.Project(sample.Position);
                if (!pixel.HasValue)
                {
                    continue;
                }
                var colour = TextureSampling.Bilinear(pixels, bitmap.Width, bitmap.Height, pixel.Value.X, pixel.Value.Y, skyGuard);
                var value = new V3(swap ? colour.Blue : colour.Red, colour.Green, swap ? colour.Red : colour.Blue);
                nodes[sample.Node].Colour += value * sample.Weight;
                nodes[sample.Node].Weight += sample.Weight;
            }
            GC.KeepAlive(bitmap);
        }
        foreach (var node in nodes)
        {
            if (node.Weight > 0)
            {
                node.Colour /= node.Weight;
            }
        }
    }

    private V3[] GlobalOffsets(List<TextureSeamNode> nodes, List<TextureSeamLink> links, int images, CancellationToken cancellation)
    {
        var offsets = new V3[images];
        var sums = new V3[images];
        var weights = new int[images];
        foreach (var link in links)
        {
            weights[nodes[link.First].Image]++;
            weights[nodes[link.Second].Image]++;
        }
        // Fit image exposure offsets to many samples along seams. Damping and
        // a small zero anchor fix the gauge and avoid bipartite oscillation.
        for (var pass = 0; pass < 160; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            Array.Clear(sums);
            foreach (var link in links)
            {
                var a = nodes[link.First];
                var b = nodes[link.Second];
                sums[a.Image] += offsets[b.Image] + b.Colour - a.Colour;
                sums[b.Image] += offsets[a.Image] + a.Colour - b.Colour;
            }
            for (var image = 0; image < images; image++)
            {
                if (weights[image] > 0)
                {
                    offsets[image] = Clamp((offsets[image] + sums[image] / (weights[image] * 1.001)) * .5, 48);
                }
            }
        }
        return offsets;
    }

    private V3[] LocalOffsets(List<TextureSeamNode> nodes, V3[] global, CancellationToken cancellation)
    {
        var local = new V3[nodes.Count];
        foreach (var group in Enumerable.Range(0, nodes.Count).Where(index => nodes[index].Weight > 0).GroupBy(index => nodes[index].Vertex))
        {
            var mean = new V3();
            var count = 0;
            foreach (var index in group)
            {
                mean += nodes[index].Colour + global[nodes[index].Image];
                count++;
            }
            mean /= count;
            foreach (var index in group)
            {
                local[index] = Clamp(mean - nodes[index].Colour - global[nodes[index].Image], 32);
            }
        }
        var next = new V3[local.Length];
        for (var pass = 0; pass < 32; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                if (node.Weight > 0)
                {
                    next[index] = local[index];
                    continue;
                }
                var sum = new V3();
                foreach (var neighbour in node.Neighbours)
                {
                    sum += local[neighbour];
                }
                next[index] = sum / (node.Neighbours.Count + .05);
            }
            var previous = local;
            local = next;
            next = previous;
        }
        return local;
    }

    private V3 Clamp(V3 value, double limit) => new(Math.Clamp(value.X, -limit, limit),
        Math.Clamp(value.Y, -limit, limit), Math.Clamp(value.Z, -limit, limit));
}
