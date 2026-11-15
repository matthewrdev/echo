using System.ComponentModel.Composition;
using Echo.Imaging;

namespace Echo.SceneSemantics;

[Export(typeof(ISceneSegmenter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SceneSegmenter : ISceneSegmenter
{
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public SceneSegmenter(IRasterImageService images)
    {
        this.images = images;
    }

    public ISceneSegmentationSession Open(string modelPath) => new SceneSegmentationSession(modelPath, images);
}
