namespace Echo.SceneSemantics;

public interface ISceneSegmenter
{
    ISceneSegmentationSession Open(string modelPath);
}
