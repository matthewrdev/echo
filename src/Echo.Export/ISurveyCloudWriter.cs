namespace Echo.Export;

public interface ISurveyCloudWriter
{
    SurveyExportCapabilities Inspect();
    void CheckAvailable(PointCloudExportFormat format, bool projected);
    void Write(string source, string destination, PointCloudExportFormat format, string? projectedCrs);
}
