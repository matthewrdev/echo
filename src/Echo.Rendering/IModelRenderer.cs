namespace Echo.Rendering;

public interface IModelRenderer
{
    ModelRenderResult Render(ModelRenderRequest request, CancellationToken cancellation = default);
}
