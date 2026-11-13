using System.ComponentModel.Composition;
using System.Numerics;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using Echo.Metadata;

namespace Echo.Cli;

[Export(typeof(IRuntimeInfoProvider)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RuntimeInfoProvider : IRuntimeInfoProvider
{
    private readonly IMetadataReader metadata;

    [ImportingConstructor]
    public RuntimeInfoProvider(IMetadataReader metadata)
    {
        this.metadata = metadata;
    }

    public RuntimeInfo Read()
    {
        var assembly = typeof(RuntimeInfoProvider).Assembly;
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = loaded.GetName();
            var relevant = loaded == assembly || name.Name?.StartsWith("Echo.", StringComparison.Ordinal) == true
                || name.Name == "MathNet.Numerics" || name.Name == "SkiaSharp";
            if (relevant)
            {
                versions[name.Name!] = name.Version?.ToString() ?? "Unknown";
            }
        }

        return new RuntimeInfo
        {
            Framework = RuntimeInformation.FrameworkDescription,
            RuntimeVersion = Environment.Version.ToString(),
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OperatingSystemArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            BuildConfiguration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Unknown",
            Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown",
            Executable = Environment.ProcessPath ?? "Unknown",
            DynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported,
            DynamicCodeCompiled = RuntimeFeature.IsDynamicCodeCompiled,
            ServerGC = GCSettings.IsServerGC,
            GCLatency = GCSettings.LatencyMode.ToString(),
            GCAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ProcessorCount = Environment.ProcessorCount,
            Is64BitProcess = Environment.Is64BitProcess,
            VectorAccelerated = Vector.IsHardwareAccelerated,
            VectorFloatLanes = Vector<float>.Count,
            ArmAdvSimd = AdvSimd.IsSupported,
            X86Avx2 = Avx2.IsSupported,
            ExifTool = metadata.FindExifTool(),
            Assemblies = versions
        };
    }
}
