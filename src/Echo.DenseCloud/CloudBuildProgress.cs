using System.Diagnostics;
using Echo.Models;

namespace Echo.DenseCloud;

internal sealed class CloudBuildProgress
{
    private readonly int totalImages;
    private readonly int totalRows;
    private readonly Action<DenseCloudProgress>? progress;
    private readonly Action<DenseCloudReport>? preview;
    private readonly Func<DenseCloudData, DenseCloudReport> createReport;
    private readonly Stopwatch progressClock = Stopwatch.StartNew();
    private readonly Stopwatch previewClock = Stopwatch.StartNew();
    private int completedRows;

    public CloudBuildProgress(int totalImages, int totalRows, Action<DenseCloudProgress>? progress,
        Action<DenseCloudReport>? preview, Func<DenseCloudData, DenseCloudReport> createReport)
    {
        this.totalImages = totalImages;
        this.totalRows = totalRows;
        this.progress = progress;
        this.preview = preview;
        this.createReport = createReport;
    }

    public void Advance(string imageId, int imageIndex, int rows, bool imageFinished, DenseCloudData cloud)
    {
        completedRows += rows;
        if (progressClock.ElapsedMilliseconds >= 500 || imageFinished)
        {
            progress?.Invoke(new DenseCloudProgress
            {
                ImageId = imageId,
                ImageNumber = imageIndex + 1,
                TotalImages = totalImages,
                CompletedRows = completedRows,
                TotalRows = totalRows,
                RetainedPoints = cloud.Samples.Count
            });
            progressClock.Restart();
        }

        var previewDue = previewClock.ElapsedMilliseconds >= 3000 || completedRows == totalRows;
        var hasPreview = preview != null && cloud.Samples.Count > 0;
        if (hasPreview && previewDue)
        {
            preview!(createReport(cloud));
            previewClock.Restart();
        }
    }
}
