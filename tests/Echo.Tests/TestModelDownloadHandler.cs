using System.Net;

namespace Echo.Tests;

public sealed class TestModelDownloadHandler : HttpMessageHandler
{
    private readonly byte[] content;
    private int requests;

    public TestModelDownloadHandler(byte[] content)
    {
        this.content = content;
    }

    public int Requests => requests;
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Func<CancellationToken, Task>? BeforeResponse
    {
        get; set;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        Interlocked.Increment(ref requests);
        if (BeforeResponse != null)
        {
            await BeforeResponse(cancellation);
        }

        return new HttpResponseMessage(Status) { Content = new ByteArrayContent(content) };
    }
}
