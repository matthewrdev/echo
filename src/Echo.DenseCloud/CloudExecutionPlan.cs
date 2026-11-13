namespace Echo.DenseCloud;

public readonly record struct CloudExecutionPlan(int Workers, int RowsPerBatch, long MaximumScratchBytes);
