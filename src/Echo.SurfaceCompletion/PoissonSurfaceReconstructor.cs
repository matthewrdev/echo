using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using Echo.Contracts;
using Echo.Storage;

namespace Echo.SurfaceCompletion;

[Export(typeof(ISurfaceReconstructor)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PoissonSurfaceReconstructor : ISurfaceReconstructor
{
    private readonly IPoissonSurfaceCodec codec;

    [ImportingConstructor]
    public PoissonSurfaceReconstructor(IPoissonSurfaceCodec codec)
    {
        this.codec = codec;
    }

    public Surface Reconstruct(Surface measured, ReconstructionScene scene, double voxelSize,
        CancellationToken cancellation, Action<string>? progress = null, ProcessingMemoryTracker? memory = null)
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
        var samples = OrientedSurfaceSamples.Build(measured, scene, cancellation);
        if (samples.Length < 3)
        {
            throw new EchoException("Too few oriented samples for surface completion.", 5);
        }
        var width = Math.Max(samples.Max(sample => sample.Position.X) - samples.Min(sample => sample.Position.X),
            Math.Max(samples.Max(sample => sample.Position.Y) - samples.Min(sample => sample.Position.Y),
                samples.Max(sample => sample.Position.Z) - samples.Min(sample => sample.Position.Z)));
        var depth = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(width * 1.25 / voxelSize, 1))), 5, 12);
        var directory = Path.Combine(Path.GetTempPath(), "echo-poisson-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "samples.bin");
            var output = Path.Combine(directory, "surface.bin");
            codec.WriteSamples(input, samples, cancellation);
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
            return codec.Read(output, cancellation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
