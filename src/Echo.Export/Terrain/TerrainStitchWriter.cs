using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Models;
using SkiaSharp;

namespace Echo.Export.Terrain;

internal sealed class TerrainStitchWriter : ITerrainStitchWriter
{
    private readonly TerrainRasterReader raster;
    private readonly TerrainWallReader wallReader;
    private readonly TerrainTextureBlender textureBlender;
    private readonly TerrainTexturedGlbWriter glb;
    private readonly IFileOperations files;

    public TerrainStitchWriter(TerrainRasterReader raster, TerrainWallReader wallReader, TerrainTextureBlender textureBlender,
        TerrainTexturedGlbWriter glb, IFileOperations files)
    {
        this.raster = raster;
        this.wallReader = wallReader;
        this.textureBlender = textureBlender;
        this.glb = glb;
        this.files = files;
    }

    public void Write(string elevation, string imagery, string altitudeDatum, IReadOnlyList<TerrainWallInput> walls, TerrainRasterLayers layers,
        string output, string work, int gridLongEdge, int textureSize, double maxSeamError, CancellationToken cancellation)
    {
        var grid = raster.Read(elevation, imagery, altitudeDatum, gridLongEdge, textureSize, work, layers);
        var positions = new List<V3>(grid.Heights.Length);
        var terrainUvs = new List<double[]>(grid.Heights.Length);
        var wallUvs = new List<double[]>(grid.Heights.Length);
        var removed = new bool[(grid.Columns - 1) * (grid.Rows - 1)];
        var report = new List<object>();
        var windows = new List<TerrainGridWindow>();
        var terrainTriangles = new List<int>();
        var materials = new List<TerrainMaterial>();
        var emptyUv = new double[2];
        for (var index = 0; index < grid.Heights.Length; index++)
        {
            positions.Add(grid.Local(index));
            terrainUvs.Add(grid.Uv(index % grid.Columns, index / grid.Columns));
            wallUvs.Add(emptyUv);
        }

        using var terrainTexture = SKBitmap.Decode(grid.Imagery);
        foreach (var input in walls)
        {
            cancellation.ThrowIfCancellationRequested();
            var wall = wallReader.Read(input, grid, maxSeamError, cancellation);
            if (windows.Any(window => window.Overlaps(wall.Window)))
            {
                throw new EchoException($"{wall.Name} overlaps another wall's terrain window. Use separately cropped wall surfaces.", 2);
            }

            windows.Add(wall.Window);
            var offset = positions.Count;
            var ring = TerrainStitchRing.Build(wall, grid.Columns, offset);
            textureBlender.Blend(terrainTexture, grid, wall, offset, ring, cancellation);
            terrainTriangles.AddRange(ring);
            for (var row = wall.Window.Top; row < wall.Window.Bottom; row++)
            {
                for (var column = wall.Window.Left; column < wall.Window.Right; column++)
                {
                    if (!grid.InFootprint(column, row))
                    {
                        throw new EchoException($"{wall.Name} extends outside the terrain footprint.", 2);
                    }

                    removed[row * (grid.Columns - 1) + column] = true;
                }
            }

            positions.AddRange(wall.Positions);
            for (var index = 0; index < wall.Positions.Length; index++)
            {
                var pixel = wall.GridPositions[index];
                terrainUvs.Add(grid.Uv(pixel[0], pixel[1]));
                wallUvs.Add([wall.Uvs[index * 2], wall.Uvs[index * 2 + 1]]);
            }

            materials.Add(new TerrainMaterial(wall.Name, wall.Indices.Select(index => checked((int)index + offset)).ToArray(), wall.Texture, 1));
            report.Add(new { name = wall.Name, triangles = wall.Indices.Length / 3, maximumSeamErrorMetres = wall.SeamMax, seamRmsMetres = wall.SeamRms });
        }

        for (var row = 0; row < grid.Rows - 1; row++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < grid.Columns - 1; column++)
            {
                if (removed[row * (grid.Columns - 1) + column] || !grid.InFootprint(column, row))
                {
                    continue;
                }

                var a = row * grid.Columns + column;
                var b = a + 1;
                var c = a + grid.Columns;
                var d = c + 1;
                terrainTriangles.Add(a);
                terrainTriangles.Add(c);
                terrainTriangles.Add(b);
                terrainTriangles.Add(b);
                terrainTriangles.Add(c);
                terrainTriangles.Add(d);
            }
        }

        using var texture = terrainTexture.Encode(SKEncodedImageFormat.Jpeg, 92);
        materials.Insert(0, new TerrainMaterial("Terrain and transitions", terrainTriangles.ToArray(), texture.ToArray(), 0));
        var mesh = new TerrainMeshAsset(positions, terrainUvs, wallUvs, materials);
        glb.Write(Path.Combine(output, "cliff.glb"), layers.Footprint == null ? mesh : mesh.Compact(), layers.Attribution);
        var origin = grid.Origin;
        files.AtomicJson(Path.Combine(output, "cliff.geo.json"), new
        {
            schemaVersion = 1,
            origin,
            referenced = true,
            frame = "Local east, up, south in metres",
            axisConvention = "X east, Y up, Z south",
            geographicDatum = "WGS84 horizontal",
            verticalDatum = altitudeDatum,
            localToEcefRowMajor = GeographicCoordinates.HasEllipsoidalHeight(origin) ? CameraGeometry.LocalToEcefMatrix(origin) : null
        });
        files.AtomicJson(Path.Combine(output, "terrain-stitch.json"), new
        {
            schemaVersion = 1,
            grid.Columns,
            grid.Rows,
            terrainTexturePixels = textureSize,
            maxSeamErrorMetres = maxSeamError,
            photoOverlayCount = layers.PhotoOverlays.Count,
            clippedToFootprint = layers.Footprint != null,
            walls = report
        });
    }
}
