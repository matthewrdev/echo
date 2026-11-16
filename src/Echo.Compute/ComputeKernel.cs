namespace Echo.Compute;

public enum ComputeKernel
{
    DescriptorPairs,
    DescriptorLeft,
    DescriptorRight,
    GaussianHorizontal,
    GaussianVertical,
    DepthFilter,
    CloudSupport,
    FusionIntegrate,
    SurfaceVisibility,
    VolumeCorners,
    SemanticProject,
    SeamDiffuse,
    TextureProject,
    TexturePadding,
    EpipolarCandidates,
    EpipolarCounts,
    TriangulatePairs,
    UvWarmStart,
    VolumeIndex
}
