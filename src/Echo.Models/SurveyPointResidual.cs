namespace Echo.Models;

public sealed record SurveyPointResidual(string Id, string Type, string Status, double? ReprojectionPixels,
    double? EastMetres, double? NorthMetres, double? UpMetres, bool WithinControlTolerance, bool UsedAsControl);
