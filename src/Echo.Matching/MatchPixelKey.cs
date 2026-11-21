namespace Echo.Matching;

public readonly record struct MatchPixelKey(
    long LeftX,
    long LeftY,
    long RightX,
    long RightY);
