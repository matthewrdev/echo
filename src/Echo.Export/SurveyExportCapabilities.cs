namespace Echo.Export;

public sealed record SurveyExportCapabilities(bool Laz, bool Copc, bool E57, bool ProjectedCrs, string? Error = null);
