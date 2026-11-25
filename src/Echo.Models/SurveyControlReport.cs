namespace Echo.Models;

public sealed record SurveyControlReport(string Reference, IReadOnlyList<SurveyPointResidual> Points,
    double? CheckHorizontalRmseMetres, double? CheckVerticalRmseMetres);
