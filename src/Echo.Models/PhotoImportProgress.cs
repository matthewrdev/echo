namespace Echo.Models;

public sealed record PhotoImportProgress(string Type, int Count, int Completed, int Total, int Skipped, string? Id = null, string? Name = null);
