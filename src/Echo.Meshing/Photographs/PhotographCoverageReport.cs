namespace Echo.Meshing.Photographs;

public readonly record struct PhotographCoverageReport(int Faces, int InferredFaces, double Area, double InferredArea)
{
    public double InferredAreaPercent => Area > 0 ? 100 * InferredArea / Area : 0;
}
