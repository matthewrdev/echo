namespace Echo.Features;

public interface IScaleSpaceExtrema
{
    List<ScaleKeypoint> Find(float[][] differences, int width, int height, CancellationToken cancellation);
}
