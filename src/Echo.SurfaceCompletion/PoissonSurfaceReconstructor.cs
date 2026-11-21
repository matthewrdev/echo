using System.Diagnostics;
using System.Globalization;
using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

public sealed class PoissonSurfaceReconstructor : ISurfaceReconstructor
{
    private readonly IPoissonSurfaceCodec codec;

    public PoissonSurfaceReconstructor(IPoissonSurfaceCodec codec)
    {
        this.codec = codec;
    }

    public Surface Reconstruct(Surface measured, ReconstructionScene scene, double voxelSize,
        CancellationToken cancellation, Action<string>? progress = null, ProcessingMemoryTracker? memory = null, WorkBuffers? work = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(voxelSize > 0) || !double.IsFinite(voxelSize))
        {
            throw new ArgumentOutOfRangeException(nameof(voxelSize));
        }
        var executable = Path.Combine(AppContext.BaseDirectory, "echo-poisson");
        if (!File.Exists(executable))
        {
            throw new EchoException("The screened Poisson reconstruction worker is missing. Rebuild the processing application.", 5);
        }
        using var sampleWork = work?.CreateScope();
        // Add interior constraints only on faces spanning several reconstruction
        // cells; already dense curved surfaces retain their original sampling.
        var samples = OrientedSurfaceSamples.BuildRecords(measured, scene, cancellation, sampleWork,
            interiorSampleEdgeThreshold: voxelSize * 3);
        if (samples.Count < 3)
        {
            throw new EchoException("Too few oriented samples for surface completion.", 5);
        }
        var minimum = samples[0].Position;
        var maximum = minimum;
        foreach (var sample in samples)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = sample.Position;
            minimum = new V3(Math.Min(minimum.X, point.X), Math.Min(minimum.Y, point.Y), Math.Min(minimum.Z, point.Z));
            maximum = new V3(Math.Max(maximum.X, point.X), Math.Max(maximum.Y, point.Y), Math.Max(maximum.Z, point.Z));
        }
        var extent = maximum - minimum;
        var width = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        var depth = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(width * 1.25 / voxelSize, 1))), 5, 12);
        var directory = Path.Combine(Path.GetTempPath(), "echo-poisson-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "samples.bin");
            var output = Path.Combine(directory, "surface.bin");
            codec.WriteSamples(input, samples, cancellation);
            sampleWork?.Dispose();
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(input);
            start.ArgumentList.Add(output);
            start.ArgumentList.Add(depth.ToString(CultureInfo.InvariantCulture));
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, message) =>
            {
                if (!string.IsNullOrWhiteSpace(message.Data))
                {
                    progress?.Invoke(message.Data);
                }
            };
            process.Start();
            using var registration = memory?.RegisterWorker(process.Id);
            process.BeginOutputReadLine();
            var error = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                process.WaitForExitAsync(cancellation).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                process.WaitForExit();
                throw;
            }
            var details = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new EchoException($"Surface completion failed: {details.Trim()}", 5);
            }
            return codec.Read(output, cancellation, work);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
