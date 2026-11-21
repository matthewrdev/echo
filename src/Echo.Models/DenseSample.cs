namespace Echo.Models;

public readonly record struct DenseSample(float X, float Y, float Z, byte Red, byte Green, byte Blue, int View, float Confidence);
