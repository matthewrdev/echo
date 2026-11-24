using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Projects;

// The field order below belongs to the EPR1 project snapshot format.
internal static class ProjectModelCodec
{
    internal static Settings ReadSettings(BinaryReader reader) => new()
    {
        ExclusionRegions = reader.ReadArray(() => ReadExclusionRegion(reader)).ToList(),
        ColourGrade = reader.ReadBoolean() ? ReadColourGrade(reader) : null,
        TexturePriority = reader.ReadBoolean() ? ReadTexturePriorityPainting(reader) : null,
        ProcessingMemoryMode = (ProcessingMemoryMode)reader.ReadByte(),
        ProcessingMemoryBudgetMb = reader.ReadInt32(),
        ProcessingDiskBudgetMb = reader.ReadInt32(),
        OptimiseVegetation = reader.ReadBoolean(),
        VegetationMeshToleranceMultiplier = reader.ReadDouble(),
        VegetationTextureScale = reader.ReadDouble(),
        RecoverCliffDepth = reader.ReadBoolean(),
        DepthSuppressSky = reader.ReadBoolean(),
        ScaleInvariantFeatures = reader.ReadBoolean(),
        GuidedAlignmentPass = reader.ReadBoolean(),
        DetectLines = reader.ReadBoolean(),
        LineLimit = reader.ReadInt32(),
        LineMinimumLength = reader.ReadInt32(),
        LineGradientThreshold = reader.ReadDouble(),
        LineSplitRegions = reader.ReadBoolean(),
        FeatureWidth = reader.ReadInt32(),
        BundleIterations = reader.ReadInt32(),
        FeatureLimit = reader.ReadInt32(),
        PairWindow = reader.ReadInt32(),
        MatchAllPairs = reader.ReadBoolean(),
        GpsPairNeighbours = reader.ReadInt32(),
        FrustumPairNeighbours = reader.ReadInt32(),
        PairSearchDistance = reader.ReadDouble(),
        MatchRatioThreshold = reader.ReadDouble(),
        MatchDistanceThreshold = reader.ReadDouble(),
        ExcludeVegetationFromMatching = reader.ReadBoolean(),
        CloudWorkerThreads = reader.ReadInt32(),
        CloudBatchRows = reader.ReadInt32(),
        CloudNeighbours = reader.ReadInt32(),
        CloudMinimumSupport = reader.ReadInt32(),
        CloudDepthTolerance = reader.ReadDouble(),
        CloudReprojectionTolerancePixels = reader.ReadDouble(),
        CloudMinimumParallaxDegrees = reader.ReadDouble(),
        FusionDetailMultiplier = reader.ReadDouble(),
        FusionMemoryBudgetMb = reader.ReadInt32(),
        FusionVoxelSize = reader.ReadDouble(),
        FusionAllowCoarserResolution = reader.ReadBoolean(),
        FusionIslandMode = (IslandTrimMode)reader.ReadByte(),
        FusionIslandMinimumSizePercent = reader.ReadDouble(),
        TextureSuppressSky = reader.ReadBoolean(),
        TextureSkyMarginPixels = reader.ReadInt32(),
        TextureSeamBlendPixels = reader.ReadInt32(),
        TextureColourEnhancement = reader.ReadDouble(),
        TextureJpegQuality = reader.ReadInt32(),
        TextureChartFaces = reader.ReadInt32(),
        TextureChartAngle = reader.ReadDouble(),
        TextureGutter = reader.ReadInt32(),
        ExportValidate = reader.ReadBoolean(),
        DepthConcurrency = reader.ReadInt32(),
        ComputeBackend = (ComputeBackend)reader.ReadByte(),
        DepthBackend = reader.ReadString(),
        DepthWorkerThreads = reader.ReadInt32(),
        DepthMemoryBudgetMb = reader.ReadInt32(),
        DepthWidth = reader.ReadInt32(),
        DepthPlanes = reader.ReadInt32(),
        DepthRefinementSamples = reader.ReadInt32(),
        DepthNeighbours = reader.ReadInt32(),
        DepthRangeAutomatic = reader.ReadBoolean(),
        DepthNear = reader.ReadDouble(),
        DepthFar = reader.ReadDouble(),
        SubjectDistanceMode = (SubjectDistanceMode)reader.ReadByte(),
        SubjectDistanceLimit = reader.ReadDouble(),
        TextureSize = reader.ReadInt32(),
        FaceLimit = reader.ReadInt32(),
        MeshCompleteSurface = reader.ReadBoolean(),
        MeshCloseHull = reader.ReadBoolean(),
        MeshIslandMode = (IslandTrimMode)reader.ReadByte(),
        MeshIslandMinimumAreaPercent = reader.ReadDouble()
    };

    internal static void WriteSettings(BinaryWriter writer, Settings value)
    {
        writer.WriteCollection(value.ExclusionRegions, item => WriteExclusionRegion(writer, item));
        writer.Write(value.ColourGrade != null);
        if (value.ColourGrade is { } colourGrade)
        {
            WriteColourGrade(writer, colourGrade);
        }

        writer.Write(value.TexturePriority != null);
        if (value.TexturePriority is { } texturePriority)
        {
            WriteTexturePriorityPainting(writer, texturePriority);
        }

        writer.Write((byte)value.ProcessingMemoryMode);
        writer.Write(value.ProcessingMemoryBudgetMb);
        writer.Write(value.ProcessingDiskBudgetMb);
        writer.Write(value.OptimiseVegetation);
        writer.Write(value.VegetationMeshToleranceMultiplier);
        writer.Write(value.VegetationTextureScale);
        writer.Write(value.RecoverCliffDepth);
        writer.Write(value.DepthSuppressSky);
        writer.Write(value.ScaleInvariantFeatures);
        writer.Write(value.GuidedAlignmentPass);
        writer.Write(value.DetectLines);
        writer.Write(value.LineLimit);
        writer.Write(value.LineMinimumLength);
        writer.Write(value.LineGradientThreshold);
        writer.Write(value.LineSplitRegions);
        writer.Write(value.FeatureWidth);
        writer.Write(value.BundleIterations);
        writer.Write(value.FeatureLimit);
        writer.Write(value.PairWindow);
        writer.Write(value.MatchAllPairs);
        writer.Write(value.GpsPairNeighbours);
        writer.Write(value.FrustumPairNeighbours);
        writer.Write(value.PairSearchDistance);
        writer.Write(value.MatchRatioThreshold);
        writer.Write(value.MatchDistanceThreshold);
        writer.Write(value.ExcludeVegetationFromMatching);
        writer.Write(value.CloudWorkerThreads);
        writer.Write(value.CloudBatchRows);
        writer.Write(value.CloudNeighbours);
        writer.Write(value.CloudMinimumSupport);
        writer.Write(value.CloudDepthTolerance);
        writer.Write(value.CloudReprojectionTolerancePixels);
        writer.Write(value.CloudMinimumParallaxDegrees);
        writer.Write(value.FusionDetailMultiplier);
        writer.Write(value.FusionMemoryBudgetMb);
        writer.Write(value.FusionVoxelSize);
        writer.Write(value.FusionAllowCoarserResolution);
        writer.Write((byte)value.FusionIslandMode);
        writer.Write(value.FusionIslandMinimumSizePercent);
        writer.Write(value.TextureSuppressSky);
        writer.Write(value.TextureSkyMarginPixels);
        writer.Write(value.TextureSeamBlendPixels);
        writer.Write(value.TextureColourEnhancement);
        writer.Write(value.TextureJpegQuality);
        writer.Write(value.TextureChartFaces);
        writer.Write(value.TextureChartAngle);
        writer.Write(value.TextureGutter);
        writer.Write(value.ExportValidate);
        writer.Write(value.DepthConcurrency);
        writer.Write((byte)value.ComputeBackend);
        writer.Write(value.DepthBackend);
        writer.Write(value.DepthWorkerThreads);
        writer.Write(value.DepthMemoryBudgetMb);
        writer.Write(value.DepthWidth);
        writer.Write(value.DepthPlanes);
        writer.Write(value.DepthRefinementSamples);
        writer.Write(value.DepthNeighbours);
        writer.Write(value.DepthRangeAutomatic);
        writer.Write(value.DepthNear);
        writer.Write(value.DepthFar);
        writer.Write((byte)value.SubjectDistanceMode);
        writer.Write(value.SubjectDistanceLimit);
        writer.Write(value.TextureSize);
        writer.Write(value.FaceLimit);
        writer.Write(value.MeshCompleteSurface);
        writer.Write(value.MeshCloseHull);
        writer.Write((byte)value.MeshIslandMode);
        writer.Write(value.MeshIslandMinimumAreaPercent);
    }

    private static ExclusionRegion ReadExclusionRegion(BinaryReader reader) => new()
    {
        Id = reader.ReadString(),
        Enabled = reader.ReadBoolean(),
        Shape = (ExclusionShape)reader.ReadByte(),
        Mode = (CropRegionMode)reader.ReadByte(),
        RotationY = reader.ReadDouble(),
        CentreX = reader.ReadDouble(),
        CentreY = reader.ReadDouble(),
        CentreZ = reader.ReadDouble(),
        Width = reader.ReadDouble(),
        Height = reader.ReadDouble(),
        Depth = reader.ReadDouble(),
        Radius = reader.ReadDouble(),
        Hull = reader.ReadBoolean() ? ReadCropHull(reader) : null
    };

    private static void WriteExclusionRegion(BinaryWriter writer, ExclusionRegion value)
    {
        writer.Write(value.Id);
        writer.Write(value.Enabled);
        writer.Write((byte)value.Shape);
        writer.Write((byte)value.Mode);
        writer.Write(value.RotationY);
        writer.Write(value.CentreX);
        writer.Write(value.CentreY);
        writer.Write(value.CentreZ);
        writer.Write(value.Width);
        writer.Write(value.Height);
        writer.Write(value.Depth);
        writer.Write(value.Radius);
        writer.Write(value.Hull != null);
        if (value.Hull is { } hull)
        {
            WriteCropHull(writer, hull);
        }

    }

    private static CropHull ReadCropHull(BinaryReader reader) => new()
    {
        Vertices = reader.ReadArray(() => reader.ReadArray(reader.ReadDouble)),
        Planes = reader.ReadArray(() => ReadCropPlane(reader)),
        Tolerance = reader.ReadDouble()
    };

    private static void WriteCropHull(BinaryWriter writer, CropHull value)
    {
        writer.WriteCollection(value.Vertices, item => writer.WriteCollection(item, writer.Write));
        writer.WriteCollection(value.Planes, item => WriteCropPlane(writer, item));
        writer.Write(value.Tolerance);
    }

    private static CropPlane ReadCropPlane(BinaryReader reader) => new()
    {
        NormalX = reader.ReadDouble(),
        NormalY = reader.ReadDouble(),
        NormalZ = reader.ReadDouble(),
        Offset = reader.ReadDouble()
    };

    private static void WriteCropPlane(BinaryWriter writer, CropPlane value)
    {
        writer.Write(value.NormalX);
        writer.Write(value.NormalY);
        writer.Write(value.NormalZ);
        writer.Write(value.Offset);
    }

    private static ColourGrade ReadColourGrade(BinaryReader reader) => new()
    {
        Version = reader.ReadInt32(),
        Exposure = reader.ReadDouble(),
        Temperature = reader.ReadDouble(),
        Tint = reader.ReadDouble(),
        BlackPoint = reader.ReadDouble(),
        WhitePoint = reader.ReadDouble(),
        Contrast = reader.ReadDouble(),
        Shadows = reader.ReadDouble(),
        Highlights = reader.ReadDouble(),
        Saturation = reader.ReadDouble(),
        Vibrance = reader.ReadDouble()
    };

    private static void WriteColourGrade(BinaryWriter writer, ColourGrade value)
    {
        writer.Write(value.Version);
        writer.Write(value.Exposure);
        writer.Write(value.Temperature);
        writer.Write(value.Tint);
        writer.Write(value.BlackPoint);
        writer.Write(value.WhitePoint);
        writer.Write(value.Contrast);
        writer.Write(value.Shadows);
        writer.Write(value.Highlights);
        writer.Write(value.Saturation);
        writer.Write(value.Vibrance);
    }

    private static TexturePriorityPainting ReadTexturePriorityPainting(BinaryReader reader) => new()
    {
        AlignmentArtifact = reader.ReadString(),
        Strokes = reader.ReadArray(() => ReadTexturePriorityStroke(reader)).ToList()
    };

    private static void WriteTexturePriorityPainting(BinaryWriter writer, TexturePriorityPainting value)
    {
        writer.Write(value.AlignmentArtifact);
        writer.WriteCollection(value.Strokes, item => WriteTexturePriorityStroke(writer, item));
    }

    private static TexturePriorityStroke ReadTexturePriorityStroke(BinaryReader reader) => new()
    {
        Weight = reader.ReadDouble(),
        Samples = reader.ReadArray(() => ReadTextureBrushSample(reader)).ToList()
    };

    private static void WriteTexturePriorityStroke(BinaryWriter writer, TexturePriorityStroke value)
    {
        writer.Write(value.Weight);
        writer.WriteCollection(value.Samples, item => WriteTextureBrushSample(writer, item));
    }

    private static TextureBrushSample ReadTextureBrushSample(BinaryReader reader) => new()
    {
        Centre = reader.ReadArray(reader.ReadDouble),
        Normal = reader.ReadArray(reader.ReadDouble),
        Radius = reader.ReadDouble(),
        Depth = reader.ReadDouble()
    };

    private static void WriteTextureBrushSample(BinaryWriter writer, TextureBrushSample value)
    {
        writer.WriteCollection(value.Centre, writer.Write);
        writer.WriteCollection(value.Normal, writer.Write);
        writer.Write(value.Radius);
        writer.Write(value.Depth);
    }

    internal static Hint ReadHint(BinaryReader reader) => new()
    {
        Id = reader.ReadString(),
        Type = reader.ReadString(),
        ImageId = reader.ReadString(),
        OtherImageId = reader.ReadOptionalString(),
        Position = reader.ReadBoolean() ? ReadGeoPosition(reader) : null,
        Attitude = reader.ReadBoolean() ? ReadAttitude(reader) : null,
        Pixel = reader.ReadBoolean() ? reader.ReadArray(reader.ReadDouble) : null,
        OtherPixel = reader.ReadBoolean() ? reader.ReadArray(reader.ReadDouble) : null,
        Enabled = reader.ReadBoolean(),
        Note = reader.ReadString()
    };

    internal static void WriteHint(BinaryWriter writer, Hint value)
    {
        writer.Write(value.Id);
        writer.Write(value.Type);
        writer.Write(value.ImageId);
        writer.WriteOptionalString(value.OtherImageId);
        writer.Write(value.Position != null);
        if (value.Position is { } position)
        {
            WriteGeoPosition(writer, position);
        }

        writer.Write(value.Attitude != null);
        if (value.Attitude is { } attitude)
        {
            WriteAttitude(writer, attitude);
        }

        writer.Write(value.Pixel != null);
        if (value.Pixel is { } pixel)
        {
            writer.WriteCollection(pixel, writer.Write);
        }

        writer.Write(value.OtherPixel != null);
        if (value.OtherPixel is { } otherPixel)
        {
            writer.WriteCollection(otherPixel, writer.Write);
        }

        writer.Write(value.Enabled);
        writer.Write(value.Note);
    }

    internal static GeoPosition ReadGeoPosition(BinaryReader reader) => new()
    {
        Latitude = reader.ReadDouble(),
        Longitude = reader.ReadDouble(),
        Altitude = reader.ReadOptional(reader.ReadDouble),
        AltitudeDatum = reader.ReadString(),
        UncertaintyMetres = reader.ReadDouble()
    };

    internal static void WriteGeoPosition(BinaryWriter writer, GeoPosition value)
    {
        writer.Write(value.Latitude);
        writer.Write(value.Longitude);
        writer.WriteOptional(value.Altitude, writer.Write);
        writer.Write(value.AltitudeDatum);
        writer.Write(value.UncertaintyMetres);
    }

    internal static Attitude ReadAttitude(BinaryReader reader) => new()
    {
        Yaw = reader.ReadDouble(),
        Pitch = reader.ReadDouble(),
        Roll = reader.ReadDouble(),
        Source = reader.ReadOptionalString()
    };

    internal static void WriteAttitude(BinaryWriter writer, Attitude value)
    {
        writer.Write(value.Yaw);
        writer.Write(value.Pitch);
        writer.Write(value.Roll);
        writer.WriteOptionalString(value.Source);
    }

}
