namespace Echo.Models;

public sealed class Settings
{
    public const double MinimumFusionVoxelSize = .025;

    public List<ExclusionRegion> ExclusionRegions { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ColourGrade? ColourGrade
    {
        get; set;
    }
    public TexturePriorityPainting? TexturePriority
    {
        get; set;
    }

    public ProcessingMemoryMode ProcessingMemoryMode { get; set; } = ProcessingMemoryMode.Paged;
    public int ProcessingMemoryBudgetMb { get; set; } = 0;
    public int ProcessingDiskBudgetMb { get; set; } = 0;
    public bool OptimiseVegetation { get; set; } = true;
    public double VegetationMeshToleranceMultiplier { get; set; } = 2.5;
    public double VegetationTextureScale { get; set; } = .65;
    public bool RecoverCliffDepth { get; set; } = false;
    public bool DepthSuppressSky { get; set; } = true;
    public bool ScaleInvariantFeatures { get; set; } = true;
    public bool GuidedAlignmentPass { get; set; } = true;
    public bool DetectLines { get; set; } = true;
    public int LineLimit { get; set; } = 500;
    public int LineMinimumLength { get; set; } = 24;
    public double LineGradientThreshold { get; set; } = .015;
    public bool LineSplitRegions { get; set; } = true;
    public int FeatureWidth { get; set; } = 2000;
    public int BundleIterations { get; set; } = 50;
    public int FeatureLimit { get; set; } = 6000;
    public int PairWindow { get; set; } = 5;
    public bool MatchAllPairs
    {
        get; set;
    }
    public int GpsPairNeighbours { get; set; } = 12;
    public int FrustumPairNeighbours { get; set; } = 32;
    public double PairSearchDistance { get; set; } = 500;
    public double MatchRatioThreshold { get; set; } = .72;
    public double MatchDistanceThreshold { get; set; } = .65;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExcludeVegetationFromMatching
    {
        get; set;
    }
    public int CloudWorkerThreads { get; set; } = 0;
    public int CloudBatchRows { get; set; } = 8;
    public int CloudNeighbours { get; set; } = 16;
    public int CloudMinimumSupport { get; set; } = 2;
    public double CloudDepthTolerance { get; set; } = .01;
    public double CloudReprojectionTolerancePixels { get; set; } = 1.5;
    public double CloudMinimumParallaxDegrees { get; set; } = .5;
    public double FusionDetailMultiplier { get; set; } = 1;
    public int FusionMemoryBudgetMb { get; set; } = 0;
    public double FusionVoxelSize { get; set; } = 0;
    public bool FusionAllowCoarserResolution { get; set; } = false;
    public IslandTrimMode FusionIslandMode { get; set; } = IslandTrimMode.Main;
    public double FusionIslandMinimumSizePercent { get; set; } = .1;
    public bool TextureSuppressSky { get; set; } = true;
    public int TextureSkyMarginPixels { get; set; } = 8;
    public int TextureSeamBlendPixels { get; set; } = 4;
    public double TextureColourEnhancement { get; set; } = .5;
    public int TextureJpegQuality { get; set; } = 92;
    public int TextureChartFaces { get; set; } = 512;
    public double TextureChartAngle { get; set; } = 60;
    public int TextureGutter { get; set; } = 4;
    public bool ExportValidate { get; set; } = true;
    public int DepthConcurrency { get; set; } = 0;
    public ComputeBackend ComputeBackend { get; set; } = ComputeBackend.Auto;
    public string DepthBackend { get; set; } = "auto";
    public int DepthWorkerThreads { get; set; } = 0;
    public int DepthMemoryBudgetMb { get; set; } = 0;
    public int DepthWidth { get; set; } = 2048;
    public int DepthPlanes { get; set; } = 96;
    public int DepthRefinementSamples { get; set; } = 9;
    public int DepthNeighbours { get; set; } = 3;
    public bool DepthRangeAutomatic { get; set; } = true;
    public double DepthNear { get; set; } = 2;
    public double DepthFar { get; set; } = 100;
    public SubjectDistanceMode SubjectDistanceMode { get; set; } = SubjectDistanceMode.Auto;
    public double SubjectDistanceLimit { get; set; } = 200;
    public int TextureSize { get; set; } = 8192;
    public int FaceLimit { get; set; } = 2000000;
    public bool MeshCompleteSurface { get; set; } = true;
    public bool MeshCloseHull { get; set; } = true;
    public IslandTrimMode MeshIslandMode { get; set; } = IslandTrimMode.Main;
    public double MeshIslandMinimumAreaPercent { get; set; } = .1;

    public void Validate()
    {
        ColourGrade?.Validate();
        TexturePriority?.Validate();
        if (ExclusionRegions == null || ExclusionRegions.Any(region => region == null))
        {
            throw new EchoException("Exclusion regions must be an array of regions.", 2);
        }

        foreach (var region in ExclusionRegions)
        {
            region.Validate();
        }

        if (ExclusionRegions.Select(region => region.Id).Distinct(StringComparer.Ordinal).Count() != ExclusionRegions.Count)
        {
            throw new EchoException("Exclusion region IDs must be unique.", 2);
        }

        var invalidProcessing = !Enum.IsDefined(ProcessingMemoryMode)
            || ProcessingMemoryBudgetMb != 0 && ProcessingMemoryBudgetMb is < 256 or > 524287
            || ProcessingDiskBudgetMb < 0;
        var invalidLines = LineLimit is < 1 or > 5000 || LineMinimumLength is < 8 or > 1000;
        var invalidLineThreshold = !double.IsFinite(LineGradientThreshold) || LineGradientThreshold is < .001 or > .2;
        var invalidAlignmentSettings = BundleIterations is < 1 or > 500
            || FeatureWidth is < 64 or > 8192
            || FeatureLimit is < 32 or > 20000
            || PairWindow is < 1 or > 100;
        var invalidMatchingSettings = GpsPairNeighbours is < 0 or > 100
            || !double.IsFinite(MatchRatioThreshold)
            || MatchRatioThreshold <= 0 || MatchRatioThreshold > 1
            || !double.IsFinite(MatchDistanceThreshold)
            || MatchDistanceThreshold <= 0 || MatchDistanceThreshold > 4;
        var invalidPairSelection = FrustumPairNeighbours is < 0 or > 100
            || !double.IsFinite(PairSearchDistance)
            || PairSearchDistance is < 1 or > 5000;
        var invalidCloudExecution = CloudWorkerThreads is < 0 or > 256 || CloudBatchRows is < 1 or > 128;
        var invalidCloudSettings = CloudNeighbours is < 1 or > 64
            || CloudMinimumSupport < 1 || CloudMinimumSupport > CloudNeighbours
            || !double.IsFinite(CloudDepthTolerance)
            || CloudDepthTolerance < .001 || CloudDepthTolerance > .1;
        var invalidCloudGeometry = !double.IsFinite(CloudReprojectionTolerancePixels)
            || CloudReprojectionTolerancePixels is < .1 or > 10
            || !double.IsFinite(CloudMinimumParallaxDegrees)
            || CloudMinimumParallaxDegrees is < 0 or > 10;
        var invalidFusionSettings = !double.IsFinite(FusionDetailMultiplier)
            || FusionDetailMultiplier < .25 || FusionDetailMultiplier > 16;
        var invalidFusionMemory = FusionMemoryBudgetMb != 0 && FusionMemoryBudgetMb is < 64 or > 524287;
        var invalidFusionResolution = !double.IsFinite(FusionVoxelSize)
            || FusionVoxelSize != 0 && FusionVoxelSize < MinimumFusionVoxelSize;
        if (invalidFusionResolution)
        {
            throw new EchoException("Fusion target voxel size must be Auto (0) or at least 2.5 cm (0.025 m).", 2);
        }

        var invalidFusionIslands = !Enum.IsDefined(FusionIslandMode)
            || !double.IsFinite(FusionIslandMinimumSizePercent)
            || FusionIslandMinimumSizePercent is < 0 or > 100;
        var invalidDepthMemory = DepthMemoryBudgetMb != 0 && DepthMemoryBudgetMb is < 256 or > 524287;
        var invalidDepthExecution = DepthConcurrency is < 0 or > 16
            || DepthWorkerThreads is < 0 or > 256
            || invalidDepthMemory
            || !Enum.IsDefined(ComputeBackend)
            || DepthBackend is not ("auto" or "cpu" or "metal" or "metal-validated");
        var invalidDepthResolution = DepthWidth is < 32 or > 8192
            || DepthPlanes is < 8 or > 2048
            || DepthRefinementSamples is < 3 or > 128
            || DepthNeighbours is < 1 or > 32;
        var invalidDepthRange = !double.IsFinite(DepthNear)
            || !double.IsFinite(DepthFar)
            || DepthNear <= 0
            || DepthFar <= DepthNear;
        var invalidSubjectDistance = !Enum.IsDefined(SubjectDistanceMode)
            || !double.IsFinite(SubjectDistanceLimit) || SubjectDistanceLimit <= 0;
        var invalidOutputSettings = TextureSize is < 256 or > 8192
            || (TextureSize & (TextureSize - 1)) != 0
            || FaceLimit is < 1 or > 10000000
            || TextureSeamBlendPixels is < 0 or > 16
            || !double.IsFinite(TextureColourEnhancement)
            || TextureColourEnhancement is < 0 or > 1
            || TextureJpegQuality is < 25 or > 100
            || TextureChartFaces is < 16 or > 8192
            || TextureGutter is < 1 or > 16
            || TextureSkyMarginPixels is < 1 or > 64
            || !double.IsFinite(TextureChartAngle)
            || TextureChartAngle is < 10 or > 80;
        var invalidIslandSettings = !Enum.IsDefined(MeshIslandMode)
            || !double.IsFinite(MeshIslandMinimumAreaPercent)
            || MeshIslandMinimumAreaPercent is < 0 or > 100;
        var invalidSemanticSettings = !double.IsFinite(VegetationMeshToleranceMultiplier)
            || VegetationMeshToleranceMultiplier is < 1 or > 4
            || !double.IsFinite(VegetationTextureScale)
            || VegetationTextureScale is < .25 or > 1;
        var invalidSettings = invalidProcessing || invalidSemanticSettings || invalidAlignmentSettings
            || invalidLines
            || invalidLineThreshold
            || invalidMatchingSettings
            || invalidPairSelection
            || invalidCloudExecution
            || invalidCloudSettings
            || invalidCloudGeometry
            || invalidFusionSettings
            || invalidFusionMemory
            || invalidFusionIslands
            || invalidDepthExecution
            || invalidDepthResolution
            || invalidDepthRange
            || invalidSubjectDistance
            || invalidOutputSettings
            || invalidIslandSettings;

        if (invalidSettings)
        {
            throw new EchoException("Invalid settings. Check documented image, depth, texture and face limits.", 2);
        }
    }
}
