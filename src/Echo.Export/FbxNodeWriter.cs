using System.Text;
using Echo.Models;

namespace Echo.Export;

internal sealed class FbxNodeWriter
{
    private const uint Version = 7400;
    private readonly BinaryWriter writer;

    public FbxNodeWriter(BinaryWriter writer)
    {
        this.writer = writer;
    }

    public void Begin()
    {
        writer.Write("Kaydara FBX Binary  \0\u001a\0"u8);
        writer.Write(Version);
    }

    public void Finish()
    {
        writer.Write(new byte[13]);
        writer.Write(Convert.FromHexString("FABCAB09D0C8D466B176FB831CF7267E"));
        writer.Write(0u);
        var padding = 16 - (int)(writer.BaseStream.Position % 16);
        writer.Write(new byte[padding]);
        writer.Write(Version);
        writer.Write(new byte[120]);
        writer.Write(Convert.FromHexString("F85A8C6ADEF5D97EECE90CE3758F290B"));
    }

    public void Value(string name, params object[] values) => Node(name, null, values);

    public void Node(string name, Action? children, params object[] values)
    {
        var stream = writer.BaseStream;
        var start = stream.Position;
        var nameBytes = Encoding.ASCII.GetBytes(name);
        writer.Write(new byte[12]);
        writer.Write(checked((byte)nameBytes.Length));
        writer.Write(nameBytes);

        var properties = stream.Position;
        foreach (var value in values)
        {
            WriteProperty(value);
        }

        var propertyLength = stream.Position - properties;
        if (children != null)
        {
            children();
            writer.Write(new byte[13]);
        }

        var end = stream.Position;
        if (end > uint.MaxValue)
        {
            throw new EchoException("The FBX file exceeds the 4 GB export limit.", 2);
        }

        stream.Position = start;
        writer.Write((uint)end);
        writer.Write((uint)values.Length);
        writer.Write((uint)propertyLength);
        stream.Position = end;
    }

    private void WriteProperty(object value)
    {
        switch (value)
        {
            case int integer:
                writer.Write((byte)'I');
                writer.Write(integer);
                break;
            case long integer:
                writer.Write((byte)'L');
                writer.Write(integer);
                break;
            case double number:
                writer.Write((byte)'D');
                writer.Write(number);
                break;
            case bool boolean:
                writer.Write((byte)'C');
                writer.Write(boolean);
                break;
            case string text:
                WriteBytes((byte)'S', Encoding.UTF8.GetBytes(text));
                break;
            case byte[] bytes:
                WriteBytes((byte)'R', bytes);
                break;
            case FbxArray array:
                writer.Write(array.Type);
                writer.Write((uint)array.Count);
                writer.Write(0u); // Uncompressed array.
                writer.Write(checked((uint)((long)array.Count * array.ElementSize)));
                array.Write(writer);
                break;
            default:
                throw new ArgumentException("Unsupported FBX property type.", nameof(value));
        }
    }

    private void WriteBytes(byte type, byte[] bytes)
    {
        writer.Write(type);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }
}
