using System.Diagnostics;
using System.Security.Cryptography;
using Echo.Models;

namespace Echo.SceneSemantics;

public sealed class ModelAssetCache : IModelAssetCache
{
    private readonly string directory;
    private readonly HttpMessageHandler? handler;

    public ModelAssetCache()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        directory = Path.Combine(root, "Echo", "models");
    }

    public ModelAssetCache(string directory, HttpMessageHandler handler)
    {
        this.directory = directory;
        this.handler = handler;
    }

    public string EnsureAvailable(ModelAsset asset, string legacyDirectory, CancellationToken cancellation, Action<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var folder = Path.Combine(directory, asset.Fingerprint);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, asset.FileName);
        using var gate = Acquire(path + ".lock", cancellation, progress);
        if (Matches(path, asset, cancellation))
        {
            progress?.Invoke($"Reusing {asset.Name} from Echo's shared cache; no download needed. Cache: {path}");
            return path;
        }

        if (File.Exists(path))
        {
            progress?.Invoke($"Cached {asset.Name} failed its integrity check; replacing it with the verified model.");
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            var legacy = Path.Combine(legacyDirectory, asset.FileName);
            if (Matches(legacy, asset, cancellation))
            {
                progress?.Invoke($"Reusing this project's {asset.Name}; copying it to Echo's shared cache for other projects. No download needed.");
                File.Copy(legacy, temporary);
            }
            else
            {
                Download(asset, temporary, cancellation, progress);
            }

            if (!Matches(temporary, asset, cancellation))
            {
                throw new EchoException($"The {asset.Name} did not match its expected SHA-256 checksum and was not installed. Retry the download from {asset.DownloadUri}.", 5);
            }

            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            progress?.Invoke($"Verified {asset.Name} and saved it in Echo's shared cache. Future projects can use it offline. Cache: {path}");
            return path;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private FileStream Acquire(string path, CancellationToken cancellation, Action<string>? progress)
    {
        var waiting = Stopwatch.StartNew();
        var reported = false;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            // Unix reports EAGAIN (11 on Linux, 35 on macOS); Windows uses sharing/lock violations.
            catch (IOException error) when ((error.HResult & 0xffff) is 11 or 35 or 32 or 33)
            {
                if (waiting.Elapsed > TimeSpan.FromMinutes(25))
                {
                    throw new EchoException("Timed out waiting for another Echo process to prepare the shared scene model. Retry after that process finishes.", 5);
                }

                if (!reported)
                {
                    progress?.Invoke("Waiting for another Echo process to prepare the shared scene model; a second download will not be started.");
                    reported = true;
                }

                cancellation.WaitHandle.WaitOne(100);
            }
        }
    }

    private bool Matches(string path, ModelAsset asset, CancellationToken cancellation)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        if (stream.Length > asset.MaximumBytes)
        {
            return false;
        }

        var hash = SHA256.HashDataAsync(stream, cancellation).GetAwaiter().GetResult();
        return Convert.ToHexString(hash).Equals(asset.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private void Download(ModelAsset asset, string path, CancellationToken cancellation, Action<string>? progress)
    {
        progress?.Invoke($"Downloading {asset.Name} ({asset.DownloadSize}) from Hugging Face ({asset.DownloadUri.Host}). This file enables local sky, vegetation and rock detection. Photographs are not uploaded. The model will be cached for all projects.");
        progress?.Invoke($"Model download source: {asset.DownloadUri}");
        try
        {
            using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(20));
            using var response = client.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > asset.MaximumBytes)
            {
                throw new EchoException("Scene model download exceeded its expected size and was not installed.", 5);
            }

            using var source = response.Content.ReadAsStream(timeout.Token);
            using var target = File.Create(path);
            CopyDownload(source, target, asset, timeout.Token, progress);
        }
        catch (HttpRequestException error)
        {
            throw new EchoException($"Could not download {asset.Name} from Hugging Face: {error.Message} Check the connection and retry. To process without this model, turn off Exclude sky from depth, Recover missing cliff detail and Optimise vegetation, then start a new run.", 5);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new EchoException($"The {asset.Name} download from Hugging Face timed out. Retry when the connection is available. Completed photo classifications remain saved.", 5);
        }
    }

    private void CopyDownload(Stream source, Stream target, ModelAsset asset, CancellationToken cancellation, Action<string>? progress)
    {
        var buffer = new byte[1024 * 1024];
        long received = 0;
        long reported = 0;
        int count;
        while ((count = source.ReadAsync(buffer, cancellation).AsTask().GetAwaiter().GetResult()) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            received += count;
            if (received > asset.MaximumBytes)
            {
                throw new EchoException("Scene model download exceeded its expected size and was not installed.", 5);
            }

            target.Write(buffer, 0, count);
            if (received - reported >= 10 * 1024 * 1024)
            {
                reported = received;
                progress?.Invoke($"Downloading scene model from Hugging Face: {received / 1024 / 1024:N0} MB received ({asset.DownloadSize} total, approximately).");
            }
        }
    }
}
