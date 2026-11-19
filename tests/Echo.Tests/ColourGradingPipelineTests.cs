using System.Buffers.Binary;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ColourGradingPipelineTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IRasterImageService images;
    private readonly IGisExportService exports;
    private readonly IPipelineStage texture;

    [ImportingConstructor]
    public ColourGradingPipelineTests(IProjectStoreFactory stores, IRunContextFactory contexts,
        IFileOperations files, IRasterImageService images, IGisExportService exports, [ImportMany] IEnumerable<IPipelineStage> stages)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.images = images;
        this.exports = exports;
        texture = stages.Single(stage => stage.Name == "texture");
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Saved colour grade rebakes textures and exports LAS colours while retaining original sources and caches", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-grade-pipeline-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(Path.Combine(directory, "scan.echo"));
            store.Init("Grade integration");
            try
            {
                using var photo = new SKBitmap(128, 128);
                photo.Erase(new SKColor(128, 128, 128));
                images.Save(photo, store.PathFor("source.png"), SKEncodedImageFormat.Png);
                var source = File.ReadAllBytes(store.PathFor("source.png"));
                store.Edit(project =>
                {
                    project.Images = [new Photo { Id = "a", Name = "a.png", Source = "source.png", Width = 128, Height = 128, FocalPixels = 64 }];
                    project.Settings.ComputeBackend = ComputeBackend.Cpu;
                    project.Settings.OptimiseVegetation = false;
                    project.Settings.TextureSuppressSky = false;
                    project.Settings.TextureColourEnhancement = 0;
                    project.Settings.TextureSeamBlendPixels = 0;
                    project.Settings.TextureSize = 256;
                    project.Settings.TextureJpegQuality = 100;
                });
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), new ReconstructionScene
                {
                    Origin = new GeoPosition { Latitude = -33, Longitude = 150, Altitude = 100, AltitudeDatum = "WGS84 ellipsoid" },
                    Cameras = [new CameraPose { ImageId = "a" }],
                    Points = [new CloudPoint { Position = [0, 0, 10], Colour = [128, 128, 128] }]
                });
                files.AtomicData(store.PathFor("artifacts/mesh/test/mesh.json"), new Surface
                {
                    Vertices = [[-1, -.3, 10], [1, -.3, 10], [1, .3, 10], [-1, .3, 10]],
                    Faces = [[0, 1, 2], [0, 2, 3]],
                    SourceImages = ["a", "a"]
                });
                var original = Bake(store);
                store.Edit(project => project.Settings.ColourGrade = new ColourGrade());
                Check(Bake(store).Artifacts["texture"] == original.Artifacts["texture"], "Identity grade must reuse the original checkpoint.");
                store.Edit(project => project.Settings.ColourGrade = new ColourGrade { Exposure = 1 });
                var graded = Bake(store);
                Check(graded.Artifacts["texture"] != original.Artifacts["texture"], "Changed grade must invalidate texture.");
                Check(Bake(store).Artifacts["texture"] == graded.Artifacts["texture"], "Unchanged grade must reuse the baked texture.");
                Check(original.Artifacts["mesh"] == graded.Artifacts["mesh"], "Grading must preserve upstream geometry.");
                Check(store.GetRun(original.Id).Snapshot.Settings.ColourGrade == null, "Historical snapshots retain original colours.");
                Check(source.SequenceEqual(File.ReadAllBytes(store.PathFor("source.png"))), "Source photograph bytes must not change.");
                using var atlas = SKBitmap.Decode(store.PathFor(graded.Artifacts["texture"] + "/atlas.jpg"));
                Check(atlas.Pixels.Count(c => Math.Abs(c.Red - 176) <= 3 && Math.Abs(c.Green - 176) <= 3 && Math.Abs(c.Blue - 176) <= 3) > 100,
                    "The actual photographic atlas must contain the expected one-stop colour.");
                var exported = exports.Export(store, graded.Id, Path.Combine(directory, "export"));
                var las = File.ReadAllBytes(Path.Combine(exported.Directory, "sparse.las"));
                var start = (int)BinaryPrimitives.ReadUInt32LittleEndian(las.AsSpan(96, 4));
                Check(BinaryPrimitives.ReadUInt16LittleEndian(las.AsSpan(start + 30, 2)) == 176 * 257,
                    "Exported LAS must use the saved run's grading recipe.");
                Check(File.Exists(Path.Combine(exported.Directory, "cloud-colour-grade.json")), "Export records the applied recipe.");
                store.Edit(project => project.Settings.ColourGrade = null);
                Check(Bake(store).Artifacts["texture"] == original.Artifacts["texture"], "Reset must recover the original checkpoint exactly.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });
    }

    private RunData Bake(IProjectStore store)
    {
        var run = new RunData
        {
            Id = Guid.NewGuid().ToString("N"),
            Target = "texture",
            Snapshot = store.Load(),
            Artifacts = new Dictionary<string, string> { ["alignment"] = "artifacts/alignment/test", ["mesh"] = "artifacts/mesh/test" }
        };
        using var context = contexts.Create(store, run, default, null);
        context.Start();
        texture.Execute(context);
        context.Complete();
        return run;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
