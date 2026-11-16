using System.ComponentModel.Composition;
using System.Text;

namespace Echo.Texturing.Metal;

[Export(typeof(ITexturePlacementAccelerator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetalTexturePlacementAccelerator : ITexturePlacementAccelerator
{
    [ImportingConstructor]
    public MetalTexturePlacementAccelerator()
    {
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
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TexturePlacement.metal"));
            var details = new StringBuilder(2048);
            var pointer = MetalTextureNative.Create(source, size, details, details.Capacity);
            if (pointer == IntPtr.Zero)
            {
                throw new TextureAccelerationException(details.ToString());
            }
            return new MetalTexturePlacementSession(new MetalTextureHandle(pointer), size, details.ToString());
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            throw new TextureAccelerationException("Metal module unavailable: " + error.Message);
        }
    }
}
