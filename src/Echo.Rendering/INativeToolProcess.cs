using System.Text.Json.Serialization.Metadata;

namespace Echo.Rendering;

public interface INativeToolProcess
{
    TResult Invoke<TRequest, TResult>(string? operation, TRequest request, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResult> resultType, CancellationToken cancellation = default);
}
