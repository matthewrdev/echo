namespace Echo.Geometry;

public interface IFrustumOverlap
{
    double Score(CaptureFrustum leftView, CaptureFrustum rightView);
}
