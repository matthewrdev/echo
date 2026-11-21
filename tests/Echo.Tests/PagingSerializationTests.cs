using System.Text.Json;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Tests;

public sealed class PagingSerializationTests
{
    private readonly IBulkArtifactStore artifacts;

    public PagingSerializationTests(IBulkArtifactStore artifacts)
    {
        this.artifacts = artifacts;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Binary depth and semantic records preserve exact sample bits", () => InDirectory(directory =>
        {
            var random = new Random(41);
            var values = new List<float> { 0f, -0f, float.Epsilon, float.MinValue, float.MaxValue };
            for (var index = 0; index < 20000; index++)
            {
                var value = BitConverter.Int32BitsToSingle((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1));
                if (float.IsFinite(value))
                {
                    values.Add(value);
                }
            }
            var path = Path.Combine(directory, "depth.json");
            artifacts.Write(path, new DepthMap { Width = values.Count, Height = 1, Depth = values.ToArray(), Confidence = values.ToArray() });
            using var session = artifacts.OpenSession(65536, 0);
            var saved = session.Read<DepthMap>(path);
            for (var index = 0; index < values.Count; index++)
            {
                Check(BitConverter.SingleToInt32Bits(saved.DepthAt(index)) == BitConverter.SingleToInt32Bits(values[index]), "Depth precision changed");
                Check(BitConverter.SingleToInt32Bits(saved.ConfidenceAt(index)) == BitConverter.SingleToInt32Bits(values[index]), "Confidence precision changed");
            }
            var semanticPath = Path.Combine(directory, "semantics.json");
            artifacts.Write(semanticPath, new SemanticMap { Width = 3, Height = 1, Vegetation = [0, 128, 255], Rock = [255, 1, 0] });
            var semantics = session.Read<SemanticMap>(semanticPath);
            Check(semantics.VegetationSamples.SequenceEqual(new byte[] { 0, 128, 255 }) && semantics.RockSamples.SequenceEqual(new byte[] { 255, 1, 0 }), "Semantic probabilities changed");
            Check(semantics.SkySamples.Count == 0, "Legacy semantic artifacts without sky remain readable");
            var skyPath = Path.Combine(directory, "sky-semantics.json");
            artifacts.Write(skyPath, new SemanticMap { Width = 3, Height = 1, Vegetation = [0, 128, 255], Rock = [255, 1, 0], Sky = [0, 90, 255] });
            Check(session.Read<SemanticMap>(skyPath).SkySamples.SequenceEqual(new byte[] { 0, 90, 255 }), "Paged sky probabilities survive serialization");
            Check(artifacts.Read<SemanticMap>(skyPath).Sky.SequenceEqual(new byte[] { 0, 90, 255 }), "Materialized sky probabilities survive serialization");
        }));
        tests.Run("Binary UV records preserve precision and reject truncated buffers", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "uvs.json");
            artifacts.Write(path, new TextureCoordinates { Values = [[Math.PI, -0d], [double.Epsilon, 1d]] });
            var result = artifacts.Read<TextureCoordinates>(path);
            Check(result.Values[0][0] == Math.PI && BitConverter.DoubleToInt64Bits(result.Values[0][1]) == long.MinValue, "UV bits changed");
            var buffer = Directory.GetFiles(directory, "*.bin").Single();
            using (var stream = File.OpenWrite(buffer))
            {
                stream.SetLength(20);
            }
            Reject(() => artifacts.Read<TextureCoordinates>(path));
        }));
        tests.Run("Obsolete bulk JSON is rejected instead of silently converted", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "volume.json");
            File.WriteAllText(path, "{\"voxelSize\":1,\"samples\":[]}");
            Reject(() => artifacts.Read<FusedVolume>(path));
        }));
        tests.Run("Cancelled binary publication leaves no partial metadata or buffers", () => InDirectory(directory =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                artifacts.Write(Path.Combine(directory, "uvs.json"), new TextureCoordinates { Values = [[1d, 2d]] }, cancellation.Token);
                throw new InvalidOperationException("Cancelled write succeeded");
            }
            catch (OperationCanceledException)
            {
            }
            Check(Directory.GetFiles(directory).Length == 0, "Cancelled publication leaked files");
        }));
        tests.Run("Scratch exhaustion stops before exceeding the configured disk limit", () => InDirectory(directory =>
        {
            using var session = artifacts.OpenSession(65536, 65536);
            var records = session.Open(Path.Combine(directory, "records.bin"), new IntRecordCodec(), true);
            try
            {
                for (var index = 0; index < 20000; index++)
                {
                    records.Add(index);
                }
                throw new InvalidOperationException("Disk budget was ignored");
            }
            catch (EchoException error) when (error.ExitCode == 5)
            {
                Check(session.Statistics.ScratchBytes == 65536, "Scratch allocation exceeded its cap");
                Check(records[0] == 0 && records[records.Count - 1] == records.Count - 1, "Previously written records were damaged");
            }
        }));
    }

    private void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or JsonException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid artifact was accepted");
    }

    private void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "echo-binary-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
