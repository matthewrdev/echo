using System.Text.Json.Serialization;
using Echo.Models;

namespace Echo.Pipeline;

public sealed class DenseCloudInputs
{
    public DenseCloudInputs(IReadOnlyList<string> depthUnits, string alignment, Settings settings,
        string computeImplementation, int cloudVersion = 9)
    {
        DepthUnits = depthUnits;
        Alignment = alignment;
        CloudVersion = cloudVersion;
        // Versions 5–7 added crop masks without changing uncropped validation.
        // Version 4 predates cropping. Only their full-cloud keys are compatible.
        ExclusionRegions = cloudVersion is >= 5 and <= 7 ? [] : null;
        ComputeBackend = settings.ComputeBackend;
        ComputeImplementation = computeImplementation;
        CloudNeighbours = settings.CloudNeighbours;
        CloudMinimumSupport = settings.CloudMinimumSupport;
        CloudDepthTolerance = settings.CloudDepthTolerance;
        CloudReprojectionTolerancePixels = settings.CloudReprojectionTolerancePixels;
        CloudMinimumParallaxDegrees = settings.CloudMinimumParallaxDegrees;
    }

    // Keep the historical property order: serialized input bytes are checkpoint keys.
    public IReadOnlyList<string> DepthUnits
    {
        get;
    }
    public string Alignment
    {
        get;
    }
    public int CloudVersion
    {
        get;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ExclusionRegion>? ExclusionRegions
    {
        get;
    }
    public ComputeBackend ComputeBackend
    {
        get;
    }
    public string ComputeImplementation
    {
        get;
    }
    public int CloudNeighbours
    {
        get;
    }
    public int CloudMinimumSupport
    {
        get;
    }
    public double CloudDepthTolerance
    {
        get;
    }
    public double CloudReprojectionTolerancePixels
    {
        get;
    }
    public double CloudMinimumParallaxDegrees
    {
        get;
    }
}
