namespace Echo.Contracts;

public sealed class Settings
{
    public bool ScaleInvariantFeatures { get; set; } = true;
    public bool GuidedAlignmentPass { get; set; } = true;
    public bool DetectLines { get; set; } = true;
    public int LineLimit { get; set; } = 500;
    public int LineMinimumLength { get; set; } = 24;
    public double LineGradientThreshold { get; set; } = .015;
    public bool LineSplitRegions { get; set; } = true;
    public int FeatureWidth { get; set; } = 1000;
    public int BundleIterations { get; set; } = 25;
    public int FeatureLimit { get; set; } = 800;
    public int PairWindow { get; set; } = 5;
    public bool MatchAllPairs
    {
        get; set;
    }
    public int GpsPairNeighbours { get; set; } = 3;
    public int FrustumPairNeighbours { get; set; } = 12;
    public double PairSearchDistance { get; set; } = 200;
    public double MatchRatioThreshold { get; set; } = .72;
    public double MatchDistanceThreshold { get; set; } = .65;
    public int CloudWorkerThreads { get; set; } = 0;
    public int CloudBatchRows { get; set; } = 8;
    public int CloudNeighbours { get; set; } = 8;
    public int CloudMinimumSupport { get; set; } = 1;
    public double CloudDepthTolerance { get; set; } = .01;
    public double CloudReprojectionTolerancePixels { get; set; } = 1.5;
    public double CloudMinimumParallaxDegrees { get; set; } = .5;
    public double FusionDetailMultiplier { get; set; } = 1;
    public int TextureJpegQuality { get; set; } = 92;
    public int TextureChartFaces { get; set; } = 512;
    public double TextureChartAngle { get; set; } = 60;
    public int TextureGutter { get; set; } = 4;
    public bool ExportValidate { get; set; } = true;
    public int DepthConcurrency { get; set; } = 2;
    public string DepthBackend { get; set; } = "cpu";
    public int DepthWorkerThreads { get; set; } = 0;
    public int DepthMemoryBudgetMb { get; set; } = 0;
    public int DepthWidth { get; set; } = 1024;
    public int DepthPlanes { get; set; } = 96;
    public int DepthRefinementSamples { get; set; } = 9;
    public int DepthNeighbours { get; set; } = 3;
    public bool DepthRangeAutomatic { get; set; } = true;
    public double DepthNear { get; set; } = 2;
    public double DepthFar { get; set; } = 100;
    public int TextureSize { get; set; } = 8192;
    public int FaceLimit { get; set; } = 1500000;

    public void Validate()
    {
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
        var invalidDepthMemory = DepthMemoryBudgetMb != 0 && DepthMemoryBudgetMb is < 256 or > 32768;
        var invalidDepthExecution = DepthConcurrency is < 1 or > 16
            || DepthWorkerThreads is < 0 or > 256
            || invalidDepthMemory;
        var invalidDepthResolution = DepthWidth is < 32 or > 8192
            || DepthPlanes is < 8 or > 2048
            || DepthRefinementSamples is < 3 or > 128
            || DepthNeighbours is < 1 or > 32;
        var invalidDepthRange = !double.IsFinite(DepthNear)
            || !double.IsFinite(DepthFar)
            || DepthNear <= 0
            || DepthFar <= DepthNear;
        var invalidOutputSettings = TextureSize is < 256 or > 8192
            || (TextureSize & (TextureSize - 1)) != 0
            || FaceLimit is < 1 or > 10000000
            || TextureJpegQuality is < 25 or > 100
            || TextureChartFaces is < 16 or > 8192
            || TextureGutter is < 1 or > 16
            || !double.IsFinite(TextureChartAngle)
            || TextureChartAngle is < 10 or > 80;
        var invalidSettings = invalidAlignmentSettings
            || invalidLines
            || invalidLineThreshold
            || invalidMatchingSettings
            || invalidPairSelection
            || invalidCloudExecution
            || invalidCloudSettings
            || invalidCloudGeometry
            || invalidFusionSettings
            || invalidDepthExecution
            || invalidDepthResolution
            || invalidDepthRange
            || invalidOutputSettings;

        if (invalidSettings)
        {
            throw new EchoException("Invalid settings. Check documented image, depth, texture and face limits.", 2);
        }
    }
}
