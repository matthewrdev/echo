using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Compute.Metal;

namespace Echo.Tests;

[Export]
public sealed class ComputeContractTests
{
    internal void Run(TestSuite tests)
    {
        var assembly = typeof(MetalComputeDevice).Assembly;
        var sources = assembly.GetManifestResourceNames().Where(name => name.EndsWith(".metal", StringComparison.Ordinal))
            .ToDictionary(name => name[(name.LastIndexOf("Native.", StringComparison.Ordinal) + 7)..], name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }, StringComparer.Ordinal);
        tests.Run("Every typed compute kernel resolves to a packaged native entry point", () =>
        {
            var library = new MetalComputeLibrary(sources);
            foreach (var kernel in Enum.GetValues<ComputeKernel>())
            {
                Check(library.Source.Contains("kernel void " + MetalComputeKernel.Name(kernel) + "(", StringComparison.Ordinal), "Kernel missing: " + kernel);
            }
            try
            {
                MetalComputeKernel.Name((ComputeKernel)int.MaxValue);
                throw new InvalidOperationException("Invalid kernel was accepted");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        });
        tests.Run("Compute fingerprints invalidate only an operation and its actual shared dependencies", () =>
        {
            var original = new MetalComputeLibrary(sources);
            var changed = new Dictionary<string, string>(sources) { ["06Texture.metal"] = sources["06Texture.metal"] + "\n// texture change" };
            var texture = new MetalComputeLibrary(changed);
            foreach (var operation in Enum.GetValues<ComputeOperation>().Where(operation => operation != ComputeOperation.TexturePacking))
            {
                Check((original.Implementation(operation) != texture.Implementation(operation)) == (operation == ComputeOperation.Texture), "Texture invalidated " + operation);
            }
            changed = new Dictionary<string, string>(sources) { ["02DepthViews.metal"] = sources["02DepthViews.metal"] + "\n// projection change" };
            var projection = new MetalComputeLibrary(changed);
            foreach (var operation in Enum.GetValues<ComputeOperation>().Where(operation => operation != ComputeOperation.TexturePacking))
            {
                var dependsOnProjection = operation is ComputeOperation.Cloud or ComputeOperation.Fusion or ComputeOperation.Mesh or ComputeOperation.Alignment;
                Check((original.Implementation(operation) != projection.Implementation(operation)) == dependsOnProjection, "Projection dependency incorrect for " + operation);
            }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
