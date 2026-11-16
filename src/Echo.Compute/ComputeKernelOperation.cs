namespace Echo.Compute;

internal static class ComputeKernelOperation
{
    public static ComputeOperation For(ComputeKernel kernel)
    {
        return kernel switch
        {
            ComputeKernel.GaussianHorizontal or ComputeKernel.GaussianVertical => ComputeOperation.Features,
            ComputeKernel.DescriptorPairs or ComputeKernel.DescriptorLeft or ComputeKernel.DescriptorRight => ComputeOperation.Matching,
            ComputeKernel.EpipolarCandidates or ComputeKernel.EpipolarCounts or ComputeKernel.TriangulatePairs => ComputeOperation.Alignment,
            ComputeKernel.DepthFilter => ComputeOperation.Depth,
            ComputeKernel.CloudSupport => ComputeOperation.Cloud,
            ComputeKernel.FusionIntegrate => ComputeOperation.Fusion,
            ComputeKernel.SurfaceVisibility or ComputeKernel.VolumeCorners or ComputeKernel.SemanticProject => ComputeOperation.Mesh,
            ComputeKernel.SeamDiffuse or ComputeKernel.TextureProject or ComputeKernel.TexturePadding or ComputeKernel.UvWarmStart => ComputeOperation.Texture,
            _ => throw new ArgumentOutOfRangeException(nameof(kernel))
        };
    }
}
