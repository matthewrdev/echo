namespace Echo.Validation;

public enum QualityMetric
{
    RegisteredCameraFraction,
    MedianReprojectionErrorPixels,
    SparsePoints,
    DenseSamples,
    MeanDepthCoverage,
    LowerDecileDepthCoverage,
    MeshFaces,
    MeshArea,
    InferredColourAreaFraction,
    PhotographedColourArea,
    MeshExtentX,
    MeshExtentY,
    MeshExtentZ,
    ExportBytes,
    MainHullBoundaryEdges,
    MainHullInteriorHoles,
    MainHullNonManifoldEdges,
    MainHullWindingConflicts,
    MainHullDegenerateFaces
}
