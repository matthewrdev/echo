using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;

namespace Echo.Tests;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TexturePriorityPipelineTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IPipelineStage texture;

    [ImportingConstructor]
    public TexturePriorityPipelineTests(IProjectStoreFactory stores, IRunContextFactory contexts,
        IFileOperations files, [ImportMany] IEnumerable<IPipelineStage> stages)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        texture = stages.Single(stage => stage.Name == "texture");
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Texture stage rebakes painted priorities, reuses unchanged painting and preserves earlier runs", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "echo-priority-pipeline-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(directory);
            store.Init("Texture priority test");
            try
            {
                store.Edit(project =>
                {
                    project.Settings.ComputeBackend = ComputeBackend.Cpu;
                    project.Settings.TextureSize = 256;
                });
                files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), new ReconstructionScene());
                files.AtomicData(store.PathFor("artifacts/mesh/test/mesh.json"), new Surface
                {
                    Vertices = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]],
                    Faces = [[0, 1, 2], [0, 2, 3]],
                    SourceImages = ["", ""]
                });
                var original = Bake(store);
                store.Edit(project => project.Settings.TexturePriority = new TexturePriorityPainting
                {
                    AlignmentArtifact = "artifacts/alignment/test",
                    Strokes = [new TexturePriorityStroke { Weight = 4, Samples = [new TextureBrushSample { Radius = .8, Depth = .2 }] }]
                });
                var painted = Bake(store);
                var repeated = Bake(store);
                if (original.Artifacts["texture"] == painted.Artifacts["texture"] || painted.Artifacts["texture"] != repeated.Artifacts["texture"])
                {
                    throw new Exception("Painting must change the texture checkpoint, with deterministic reuse afterward.");
                }
                if (store.GetRun(original.Id).Snapshot.Settings.TexturePriority != null
                    || original.Artifacts["mesh"] != painted.Artifacts["mesh"])
                {
                    throw new Exception("Existing snapshots and upstream geometry must stay unchanged.");
                }
                if (!File.Exists(store.PathFor(painted.Artifacts["texture"] + "/atlas.jpg")))
                {
                    throw new Exception("Painted texture was not baked to disk.");
                }
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
}
