namespace Echo.Contracts;

public sealed class SubjectDistanceRunOptions
{
    public SubjectDistanceMode Mode
    {
        get; init;
    }
    public double? MaximumDistance
    {
        get; init;
    }

    public void Apply(Settings settings)
    {
        settings.SubjectDistanceMode = Mode;
        if (MaximumDistance.HasValue)
        {
            settings.SubjectDistanceLimit = MaximumDistance.Value;
        }
    }
}
