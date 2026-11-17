namespace Echo.Fusion;

public interface IFusionGridStore
{
    FusionGrid ReadOrBuild(Func<FusionGrid> build);
}
