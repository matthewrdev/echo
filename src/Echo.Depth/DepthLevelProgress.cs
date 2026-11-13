using System.Diagnostics;

namespace Echo.Depth;

public sealed class DepthLevelProgress
{
    private readonly Action<string>? report;
    private readonly string description;
    private readonly int totalRows;
    private readonly object gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastReportMilliseconds;
    private int completedRows;

    public DepthLevelProgress(Action<string>? report, string description, int totalRows)
    {
        this.report = report;
        this.description = description;
        this.totalRows = totalRows;
    }

    public void CompleteRow()
    {
        if (report == null)
        {
            return;
        }

        lock (gate)
        {
            completedRows++;
            var elapsed = clock.ElapsedMilliseconds;
            if (elapsed - lastReportMilliseconds < 750 && completedRows != totalRows)
            {
                return;
            }

            report($"{description}: {completedRows:N0}/{totalRows:N0} rows ({completedRows * 100 / totalRows}%), {elapsed / 1000}s {elapsed % 1000:000}ms elapsed");
            lastReportMilliseconds = elapsed;
        }
    }
}
