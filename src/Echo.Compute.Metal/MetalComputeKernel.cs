namespace Echo.Compute.Metal;

internal static class MetalComputeKernel
{
    public static string Name(ComputeKernel kernel)
    {
        return kernel switch
        {
            ComputeKernel.DescriptorPairs => "descriptor_pairs",
            ComputeKernel.DescriptorLeft => "descriptor_left",
            ComputeKernel.DescriptorRight => "descriptor_right",
            ComputeKernel.GaussianHorizontal => "gaussian_horizontal",
            ComputeKernel.GaussianVertical => "gaussian_vertical",
            ComputeKernel.DepthFilter => "depth_filter",
            ComputeKernel.CloudSupport => "cloud_support",
            ComputeKernel.FusionIntegrate => "fusion_integrate",
            ComputeKernel.VolumeActiveCells => "volume_active_cells",
            ComputeKernel.VolumeCompactOffsets => "volume_compact_offsets",
            ComputeKernel.VolumeCompactGroups => "volume_compact_groups",
            ComputeKernel.VolumeCompactCells => "volume_compact_cells",
            ComputeKernel.SurfaceOcclusion => "surface_occlusion",
            ComputeKernel.SurfaceVisibility => "surface_visibility",
            ComputeKernel.VolumeIndex => "volume_index",
            ComputeKernel.VolumeCorners => "volume_corners",
            ComputeKernel.SemanticProject => "semantic_project",
            ComputeKernel.SeamDiffuse => "seam_diffuse",
            ComputeKernel.TextureProject => "texture_project",
            ComputeKernel.TexturePadding => "texture_padding",
            ComputeKernel.EpipolarCandidates => "epipolar_candidates",
            ComputeKernel.EpipolarCounts => "epipolar_counts",
            ComputeKernel.TriangulatePairs => "triangulate_pairs",
            ComputeKernel.UvWarmStart => "uv_warm_start",
            _ => throw new ArgumentOutOfRangeException(nameof(kernel))
        };
    }
}
