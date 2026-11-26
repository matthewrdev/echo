using Echo.Export;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Projects;
using Echo.Simplification;
using Echo.Storage.Artifacts;
using Echo.Texturing;
using Echo.Texturing.Seams;

namespace Echo.Pipeline.Exporting;

internal sealed class GuideTileExporter : IGuideTileExporter
{
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IGlbWriter glb;
    private readonly IGlbCompressor compressor;
    private readonly IEdgeCollapse simplifier;
    private readonly ITextureBaker baker;
    private readonly ITextureSeamLeveler seamLeveler;

    public GuideTileExporter(IFileOperations files, IJsonCodec json, IGlbWriter glb, IGlbCompressor compressor,
        IEdgeCollapse simplifier, ITextureBaker baker, ITextureSeamLeveler seamLeveler)
    {
        this.files = files;
        this.json = json;
        this.glb = glb;
        this.compressor = compressor;
        this.simplifier = simplifier;
        this.baker = baker;
        this.seamLeveler = seamLeveler;
    }

    public void Write(IProjectStore store, RunData run, string directory, TexturedRunMesh textured,
        TextureResult texture, BulkDataSession session, int depth, string? detailTile, int leafTextureSize, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (depth is < 1 or > 4)
        {
            throw new EchoException("--tile-depth must be between 1 and 4.", 2);
        }

        if (leafTextureSize is not (1024 or 2048 or 4096))
        {
            throw new EchoException("--leaf-texture-size must be 1024, 2048 or 4096.", 2);
        }

        var surface = textured.Surface;
        if (surface.Faces.Count == 0 || surface.Vertices.Count != textured.Uvs.Count)
        {
            throw new EchoException("Guide tiles require a saved textured mesh with UVs.", 2);
        }

        var bounds = GuideTileGeometry.Bounds(surface);
        var region = new GuideTileRegion(bounds[0][0], bounds[0][1], bounds[1][0], bounds[1][1]);
        var faces = Enumerable.Range(0, surface.Faces.Count).ToList();
        var root = WriteTile(directory, textured, texture, "root", 0, depth, region, faces, cancellation);
        var savedMesh = session.Read<Surface>(Artifact(store, run, "mesh", "mesh.json"));
        // Materialize before the sector workers read the same source mesh.
        var mesh = new Surface
        {
            Vertices = savedMesh.Vertices.ToArray(),
            Faces = savedMesh.Faces.ToArray(),
            SourceImages = savedMesh.SourceImages.ToArray()
        };
        var scene = json.Read<ReconstructionOverview>(Artifact(store, run, "alignment", "scene.json")).ToScene();
        var project = json.Clone(run.Snapshot);
        if (mesh.SourceImages.Count != mesh.Faces.Count || mesh.SourceImages.All(string.IsNullOrEmpty))
        {
            throw new EchoException("Leaf textures require saved photographic face assignments.", 2);
        }

        var scratch = Path.Combine(directory, ".leaf-textures");
        Directory.CreateDirectory(scratch);
        // Solve on the uncut surface, so a photo carries the same exposure and
        // vertex corrections wherever its triangles are baked for the tree.
        var corrections = seamLeveler.Compute(store, project, scene, mesh, cancellation).CornerOffsets;
        var meshFaces = Enumerable.Range(0, mesh.Faces.Count).ToList();
        root = BakeTiles(store, project, scene, directory, scratch, mesh, region, root, meshFaces, corrections, 0, leafTextureSize, cancellation);
        Directory.Delete(scratch, recursive: true);
        cancellation.ThrowIfCancellationRequested();
        var overview = Path.Combine(directory, root.Uri);
        var compressed = Path.Combine(directory, "compressed-overview.glb");
        compressor.Compress(overview, compressed);
        File.Move(compressed, overview, overwrite: true);
        root = root with
        {
            Bytes = new FileInfo(overview).Length
        };

        var comparison = detailTile == null ? null : WriteComparison(store, run, directory, textured, region, root, detailTile, mesh, scene, corrections, cancellation);

        files.AtomicJson(Path.Combine(directory, "guide-tiles.json"), new
        {
            version = 1,
            name = run.Snapshot.Name,
            up = "Y",
            runId = run.Id,
            root,
            comparison
        });
    }

    private GuideTileNode BakeTiles(IProjectStore store, ProjectData project, ReconstructionScene scene,
        string directory, string scratch, Surface mesh, GuideTileRegion region, GuideTileNode tile,
        List<int> faces, V3[] corrections, int level, int leafTextureSize, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var isLeaf = tile.Children.Count == 0;
        if (!isLeaf)
        {
            List<int>[] quadrants = [[], [], [], []];
            foreach (var face in faces)
            {
                quadrants[GuideTileGeometry.Quadrant(mesh, face, region)].Add(face);
            }

            var children = new GuideTileNode[tile.Children.Count];
            if (level == 0)
            {
                var workers = new ParallelOptions
                {
                    CancellationToken = cancellation,
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
                };
                Parallel.For(0, children.Length, workers, index =>
                {
                    var sectorScratch = Path.Combine(scratch, tile.Children[index].Id);
                    Directory.CreateDirectory(sectorScratch);
                    children[index] = BakeChild(index, json.Clone(project), sectorScratch);
                });
            }
            else
            {
                for (var index = 0; index < children.Length; index++)
                {
                    children[index] = BakeChild(index, project, scratch);
                }
            }

            GuideTileNode BakeChild(int index, ProjectData childProject, string childScratch)
            {
                var child = tile.Children[index];
                var quadrant = child.Id[^1] - '0';
                return BakeTiles(store, childProject, scene, directory, childScratch, mesh, region.Child(quadrant),
                    child, quadrants[quadrant], corrections, level + 1, leafTextureSize, cancellation);
            }

            tile = tile with
            {
                Children = children
            };
        }

        if (tile.Id == "root" && !isLeaf)
        {
            return tile;
        }

        var mismatchedGeometry = isLeaf && faces.Count != tile.Triangles;
        if (mismatchedGeometry || faces.All(face => string.IsNullOrEmpty(mesh.SourceImages[face])))
        {
            return tile;
        }

        var patch = GuideTileGeometry.Extract(mesh, faces);
        var atlas = Path.Combine(scratch, "atlas.jpg");
        var isSector = level == 1;
        project.Settings.TextureSize = isSector && !isLeaf ? Math.Min(2048, leafTextureSize) : leafTextureSize;
        var result = baker.Bake(store, project, scene, patch, atlas, cancellation,
            correctionForCorner: corner => SurfaceCorrection(faces, corrections, corner), writeAtlasVariants: false);
        var baked = result.Surface!;
        if (baked.Faces.Count != faces.Count)
        {
            return tile;
        }

        var uvs = result.Uvs;
        if (!isLeaf && baked.Faces.Count > tile.Triangles)
        {
            var reducedUvs = new List<double[]>();
            baked = BuildProxy(baked, tile.Triangles, uvs.ToList(), reducedUvs, cancellation);
            uvs = reducedUvs;
        }

        var bytes = WriteGlb(directory, tile.Uri, baked, uvs, atlas);
        return tile with
        {
            Bytes = bytes,
            Triangles = baked.Faces.Count,
            PhotographicTextureSize = result.Size
        };
    }

    private GuideTileNode WriteTile(string directory, TexturedRunMesh source, TextureResult texture,
        string id, int level, int depth, GuideTileRegion region, List<int> faceIds, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var children = new List<GuideTileNode>();
        if (level < depth && faceIds.Count >= 256)
        {
            List<int>[] quadrants = [[], [], [], []];
            foreach (var face in faceIds)
            {
                quadrants[GuideTileGeometry.Quadrant(source.Surface, face, region)].Add(face);
            }

            for (var index = 0; index < quadrants.Length; index++)
            {
                if (quadrants[index].Count > 0)
                {
                    children.Add(WriteTile(directory, source, texture, id + "-" + index, level + 1,
                        depth, region.Child(index), quadrants[index], cancellation));
                }
            }
        }

        var uvs = new List<double[]>();
        var surface = GuideTileGeometry.Extract(source.Surface, faceIds, source.Uvs, uvs);
        var bounds = GuideTileGeometry.Bounds(surface);
        var desiredSize = Math.Min(2048, 512 << level);
        var variant = texture.Variants.Where(variant => variant.Size < texture.Size &&
                File.Exists(Path.Combine(Path.GetDirectoryName(source.Atlas)!, variant.File)))
            .OrderBy(variant => Math.Abs(variant.Size - desiredSize)).FirstOrDefault();
        var atlas = texture.Size <= desiredSize || variant == null ? source.Atlas :
            Path.Combine(Path.GetDirectoryName(source.Atlas)!, variant.File);
        var error = 0d;
        if (children.Count > 0)
        {
            var target = Math.Max(128, surface.Faces.Count / (level == 0 ? 8 : 3));
            if (surface.Faces.Count > target)
            {
                var reducedUvs = new List<double[]>();
                surface = BuildProxy(surface, target, uvs, reducedUvs, cancellation);
                uvs = reducedUvs;
            }

            // A view-selection heuristic based on the tile span and overview texture density.
            var span = Math.Max(bounds[1][0] - bounds[0][0], bounds[1][1] - bounds[0][1]);
            error = Math.Max(span / 128, children.Max(child => child.Error) * 1.05);
        }

        var uri = id + ".glb";
        var bytes = WriteGlb(directory, uri, surface, uvs, atlas);
        return new GuideTileNode(id, uri, bounds, error, surface.Faces.Count, bytes, children);
    }

    private Surface BuildProxy(Surface surface, int target, List<double[]> uvs, List<double[]> outputUvs,
        CancellationToken cancellation)
    {
        const int groupFaceLimit = 32768;
        if (surface.Faces.Count <= groupFaceLimit)
        {
            return simplifier.ReduceTextured(surface, target, uvs,
                GuideTileGeometry.BoundaryVertices(surface), outputUvs, cancellation);
        }

        var groups = GuideTileGeometry.ReductionGroups(surface, groupFaceLimit, cancellation);
        var groupSurfaces = new Surface[groups.Count];
        var groupUvs = new List<double[]>[groups.Count];
        var workers = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
        };

        Parallel.For(0, groups.Count, workers, index =>
        {
            var localUvs = new List<double[]>();
            var group = GuideTileGeometry.Extract(surface, groups[index], uvs, localUvs);
            var groupTarget = Math.Max(4, (int)((long)target * group.Faces.Count / surface.Faces.Count));
            var reducedUvs = new List<double[]>();
            groupSurfaces[index] = simplifier.ReduceTextured(group, groupTarget, localUvs,
                GuideTileGeometry.BoundaryVertices(group), reducedUvs, cancellation);
            groupUvs[index] = reducedUvs;
        });

        var proxy = new Surface();
        for (var index = 0; index < groupSurfaces.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var group = groupSurfaces[index];
            var offset = proxy.Vertices.Count;
            foreach (var vertex in group.Vertices)
            {
                proxy.Vertices.Add(vertex);
            }

            foreach (var triangle in group.Faces)
            {
                proxy.Faces.Add([triangle[0] + offset, triangle[1] + offset, triangle[2] + offset]);
            }

            outputUvs.AddRange(groupUvs[index]);
        }

        return proxy;
    }

    private V3 SurfaceCorrection(IReadOnlyList<int> faces, V3[] corrections, int corner) =>
        corrections.Length == 0 ? default : corrections[faces[corner / 3] * 3 + corner % 3];

    private object WriteComparison(IProjectStore store, RunData run, string directory, TexturedRunMesh textured,
        GuideTileRegion region, GuideTileNode root, string id, Surface mesh, ReconstructionScene scene,
        V3[] corrections, CancellationToken cancellation)
    {
        var tile = root;
        var steps = new List<int>();
        foreach (var part in id.Split('-').Skip(1))
        {
            if (!int.TryParse(part, out var quadrant) || quadrant is < 0 or > 3)
            {
                throw new EchoException("Invalid detail tile ID. Use a leaf ID from guide-tiles.json.", 2);
            }

            steps.Add(quadrant);
            tile = tile.Children.FirstOrDefault(child => child.Id == tile.Id + "-" + quadrant)
                ?? throw new EchoException("Detail tile is absent from the guide hierarchy.", 2);
        }

        if (tile.Id != id || tile.Children.Count != 0)
        {
            throw new EchoException("--detail-tile must name an existing leaf in guide-tiles.json.", 2);
        }

        var patchFaces = Enumerable.Range(0, mesh.Faces.Count)
            .Where(face => GuideTileGeometry.BelongsTo(mesh, face, region, steps)).ToList();
        var patch = GuideTileGeometry.Extract(mesh, patchFaces);
        if (patch.Faces.Count == 0 || patch.SourceImages.Count != patch.Faces.Count ||
            patch.SourceImages.All(string.IsNullOrEmpty))
        {
            throw new EchoException("The selected leaf needs saved photographic face assignments.", 2);
        }

        var project = json.Clone(run.Snapshot);
        var hdUvs = new List<double[]>();
        var hdFaces = Enumerable.Range(0, textured.Surface.Faces.Count)
            .Where(face => GuideTileGeometry.BelongsTo(textured.Surface, face, region, steps));
        var hdPatch = GuideTileGeometry.Extract(textured.Surface, hdFaces, textured.Uvs, hdUvs);
        const string hdUri = "comparison-hd.glb";
        WriteGlb(directory, hdUri, hdPatch, hdUvs, textured.Atlas);
        var variants = new List<object>
        {
            new { label = "Existing HD", uri = hdUri }
        };
        var reports = new List<object>();
        foreach (var size in new[] { 2048, 4096 })
        {
            project.Settings.TextureSize = size;
            var scratch = Path.Combine(directory, ".bake");
            Directory.CreateDirectory(scratch);
            var atlas = Path.Combine(scratch, "atlas.jpg");
            var result = baker.Bake(store, project, scene, patch, atlas, cancellation,
                correctionForCorner: corner => SurfaceCorrection(patchFaces, corrections, corner), writeAtlasVariants: false);
            var uri = $"comparison-{size}.glb";
            var bytes = WriteGlb(directory, uri, result.Surface!, result.Uvs, atlas);
            variants.Add(new
            {
                label = size == 2048 ? "Photo bake 2K" : "Photo bake 4K",
                uri
            });
            reports.Add(new
            {
                size,
                bytes,
                result.CoverageFraction,
                result.TexelsPerWorldUnit
            });
            Directory.Delete(scratch, recursive: true);
        }

        var target = (new V3(tile.Bounds[0]) + new V3(tile.Bounds[1])) * .5;
        var mostUsedPhoto = patch.SourceImages.Where(image => !string.IsNullOrEmpty(image))
            .GroupBy(image => image).OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;
        var sourceCamera = scene.Cameras.First(pose => pose.ImageId == mostUsedPhoto);
        var direction = (new V3(sourceCamera.Centre) - target).Unit;
        var span = (new V3(tile.Bounds[1]) - new V3(tile.Bounds[0])).Length;
        var position = target + direction * (span * .73);
        return new
        {
            tile = id,
            bounds = tile.Bounds,
            view = new
            {
                position = position.Array,
                target = target.Array
            },
            variants,
            reports
        };
    }

    private long WriteGlb(string directory, string uri, Surface surface, IList<double[]> uvs, string atlas)
    {
        var path = Path.Combine(directory, uri);
        using var image = File.OpenRead(atlas);
        glb.Write(path, surface, uvs, image);
        return new FileInfo(path).Length;
    }

    private string Artifact(IProjectStore store, RunData run, string key, string filename)
    {
        if (run.Artifacts.TryGetValue(key, out var artifact))
        {
            var path = store.PathFor(Path.Combine(artifact, filename));
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new EchoException($"Guide tile comparison requires the saved {key} artifact.", 2);
    }
}
