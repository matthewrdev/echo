namespace Echo.Pipeline.Exporting;

internal sealed record GuideTileNode(string Id, string Uri, double[][] Bounds, double Error,
    int Triangles, long Bytes, IReadOnlyList<GuideTileNode> Children, int? PhotographicTextureSize = null);
