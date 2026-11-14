using System.ComponentModel.Composition;
using System.Text;
using System.Security.Cryptography;

namespace Echo.Depth.Metal;

[Export(typeof(IDepthCostAccelerator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class MetalDepthCostAccelerator : IDepthCostAccelerator
{
    [ImportingConstructor]
    public MetalDepthCostAccelerator()
    {
    }

    public string Backend => "metal";

    public IDepthCostSession Open(StereoView[] views, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new DepthAccelerationException("Metal requires macOS.");
        }
        try
        {
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StereoCost.metal"));
            var packedViews = new float[checked(views.Length * 24)];
            // Remove large world-coordinate offsets before reducing precision.
            // Stereo geometry depends on relative positions, not the world origin.
            var origin = views[0].Centre;
            var gray = new float[views.Sum(view => checked(view.Width * view.Height))];
            var offset = 0;
            for (var index = 0; index < views.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var view = views[index];
                var packed = packedViews.AsSpan(index * 24, 24);
                packed[0] = (float)(view.Centre.X - origin.X);
                packed[1] = (float)(view.Centre.Y - origin.Y);
                packed[2] = (float)(view.Centre.Z - origin.Z);
                for (var element = 0; element < 9; element++)
                {
                    packed[3 + element] = (float)view.WorldToCamera[element];
                }
                packed[12] = (float)view.PatchRight.X;
                packed[13] = (float)view.PatchRight.Y;
                packed[14] = (float)view.PatchRight.Z;
                packed[15] = (float)view.PatchDown.X;
                packed[16] = (float)view.PatchDown.Y;
                packed[17] = (float)view.PatchDown.Z;
                packed[18] = (float)view.Focal;
                packed[19] = view.Width;
                packed[20] = view.Height;
                packed[21] = BitConverter.Int32BitsToSingle(offset);
                var count = checked(view.Width * view.Height);
                view.Gray.AsSpan(0, count).CopyTo(gray.AsSpan(offset));
                offset += count;
            }
            var details = new StringBuilder(2048);
            var pointer = MetalDepthNative.Create(source, packedViews, views.Length, gray, gray.Length, details, details.Capacity);
            if (pointer == IntPtr.Zero)
            {
                throw new DepthAccelerationException(details.ToString());
            }
            var shaderHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            return new MetalDepthCostSession(new MetalDepthHandle(pointer), details.ToString(), shaderHash, origin);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            throw new DepthAccelerationException("Metal module unavailable: " + error.Message);
        }
    }
}
