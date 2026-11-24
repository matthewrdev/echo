using Echo.Models;

namespace Echo.Rendering;

public sealed class NativeModelRenderer : IModelRenderer
{
    private readonly INativeToolProcess native;

    public NativeModelRenderer(INativeToolProcess native)
    {
        this.native = native;
    }

    public ModelRenderResult Render(ModelRenderRequest request, CancellationToken cancellation = default)
    {
        request = request with
        {
            Input = Path.GetFullPath(request.Input),
            Output = Path.GetFullPath(request.Output)
        };
        request.Validate();
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new EchoException("Rendering requires macOS and a Metal-capable GPU.", 5);
        }

        var directory = Path.GetDirectoryName(request.Output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".echo-render-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var result = native.Invoke(null, request with
            {
                Output = temporary
            }, RenderJsonContext.Default.ModelRenderRequest, RenderJsonContext.Default.ModelRenderResult, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var invalidResult = result.Input != request.Input || result.Output != temporary
                || result.Width != request.Width || result.Height != request.Height
                || result.View != request.View || result.Mode != request.Mode
                || result.CameraImageId != request.Camera?.ImageId
                || result.Vertices <= 0 || result.Triangles <= 0 || !File.Exists(temporary);
            if (invalidResult)
            {
                throw new EchoException("The native renderer returned an incomplete result.", 5);
            }

            File.Move(temporary, request.Output, overwrite: false);
            return result with
            {
                Output = request.Output
            };
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
