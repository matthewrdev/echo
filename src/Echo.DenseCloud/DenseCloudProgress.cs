namespace Echo.DenseCloud;

public sealed class DenseCloudProgress
{
    public string ImageId { get; init; } = "";
    public int ImageNumber
    {
        get; init;
    }
    public int TotalImages
    {
        get; init;
    }
    public int CompletedRows
    {
        get; init;
    }
    public int TotalRows
    {
        get; init;
    }
    public int RetainedPoints
    {
        get; init;
    }
}
