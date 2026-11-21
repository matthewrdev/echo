namespace Echo.Models;

public sealed class CameraPose
{
    public string ImageId { get; set; } = "";
    public double[] Centre { get; set; } = [0, 0, 0];
    // Row-major camera-to-world rotation. Camera coordinates: X right, Y down, Z forward.
    public double[] Rotation { get; set; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    public string Source { get; set; } = "metadata-prior";
    public double FocalPixels
    {
        get; set;
    }
}
