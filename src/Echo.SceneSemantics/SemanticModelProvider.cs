using System.ComponentModel.Composition;
using System.Security.Cryptography;
using Echo.Contracts;

namespace Echo.SceneSemantics;

[Export(typeof(ISemanticModelProvider)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SemanticModelProvider : ISemanticModelProvider
{
    [ImportingConstructor]
    public SemanticModelProvider()
    {
    }

    public string Fingerprint => "fea729ab6c5d338bc79ad3d986b073e6faa12c97255eafd78a1f7145326e534e";

    public string EnsureAvailable(string directory, CancellationToken cancellation, Action<string>? progress = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "upernet-convnext-tiny-ade.onnx");
        if (File.Exists(path))
        {
            Verify(path);
            return path;
        }
        progress?.Invoke("Downloading the 228 MB scene classifier. Photographs remain on this computer.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx/resolve/a8e2807c3479bc617b37d8ed53e68660b3fa1ebb/model.onnx");
            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            using (var source = response.Content.ReadAsStream(cancellation))
            using (var target = File.Create(temporary))
            {
                var buffer = new byte[1024 * 1024];
                long received = 0;
                var reported = 0L;
                int count;
                while ((count = source.ReadAsync(buffer, cancellation).AsTask().GetAwaiter().GetResult()) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    target.Write(buffer, 0, count);
                    received += count;
                    if (received > 300L * 1024 * 1024)
                    {
                        throw new EchoException("Scene model download exceeded its expected size.", 5);
                    }
                    if (received - reported >= 10 * 1024 * 1024)
                    {
                        reported = received;
                        progress?.Invoke($"Scene classifier: downloaded {received / 1024 / 1024:N0} MB.");
                    }
                }
            }
            Verify(temporary);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void Verify(string path)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new EchoException($"Scene model checksum mismatch: {path}. Restore the pinned model before processing.", 5);
        }
    }
}
