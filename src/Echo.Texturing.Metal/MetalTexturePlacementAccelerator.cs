using System.Security.Cryptography;
using System.Text;
using Echo.Texturing.Layout;

namespace Echo.Texturing.Metal;

public sealed class MetalTexturePlacementAccelerator : ITexturePlacementAccelerator
{
    private readonly string source;

    public MetalTexturePlacementAccelerator()
    {
        using var stream = typeof(MetalTexturePlacementAccelerator).Assembly.GetManifestResourceStream("Echo.Texturing.Metal.Native.TexturePlacement.metal")!;
        using var reader = new StreamReader(stream);
        source = reader.ReadToEnd();
        Implementation = "echo-texture-metal-2;integer;" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public string Implementation
    {
        get;
    }

    public ITexturePlacementSession Open(int size, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new TextureAccelerationException("Metal requires macOS.");
        }
        try
        {
            var details = new StringBuilder(2048);
            var pointer = MetalTextureNative.Create(source, size, details, details.Capacity);
            if (pointer == IntPtr.Zero)
            {
                throw new TextureAccelerationException(details.ToString());
            }
            return new MetalTexturePlacementSession(new MetalTextureHandle(pointer), size, details.ToString(), Implementation);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            throw new TextureAccelerationException("Metal module unavailable: " + error.Message);
        }
    }
}
