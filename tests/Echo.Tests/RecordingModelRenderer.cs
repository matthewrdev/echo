using Echo.Rendering;

namespace Echo.Tests;

public sealed class RecordingModelRenderer : IModelRenderer
{
    public ModelRenderRequest? Request
    {
        get; private set;
    }

    public ModelRenderResult Render(ModelRenderRequest request, CancellationToken cancellation = default)
    {
        Request = request;
        return new ModelRenderResult(request.Input, request.Output, request.Width, request.Height,
            request.View, request.Mode, 3, 1);
    }
}
