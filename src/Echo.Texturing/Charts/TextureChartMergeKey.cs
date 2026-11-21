namespace Echo.Texturing.Charts;

internal readonly record struct TextureChartMergeKey(
    int First,
    int FirstVersion,
    int Second,
    int SecondVersion);
