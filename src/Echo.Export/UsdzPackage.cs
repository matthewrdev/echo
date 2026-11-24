using System.Text;
using Echo.Models;

namespace Echo.Export;

internal static class UsdzPackage
{
    public static void Write(string path, string scene, string atlas)
    {
        using var stream = File.Create(path);
        using var zip = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var crcTable = CrcTable();
        var root = WriteEntry(zip, "cliff.usda", scene, crcTable);
        var texture = WriteEntry(zip, "atlas.jpg", atlas, crcTable);
        var directory = ZipOffset(stream.Position);
        WriteDirectoryEntry(zip, root);
        WriteDirectoryEntry(zip, texture);
        var end = ZipOffset(stream.Position);
        zip.Write(0x06054b50u);
        zip.Write(0u); // Both disk numbers are zero.
        zip.Write((ushort)2);
        zip.Write((ushort)2);
        zip.Write(end - directory);
        zip.Write(directory);
        zip.Write((ushort)0);
    }

    private static UsdzEntry WriteEntry(BinaryWriter zip, string name, string path, uint[] crcTable)
    {
        using var input = File.OpenRead(path);
        var stream = zip.BaseStream;
        var offset = ZipOffset(stream.Position);
        var length = ZipOffset(input.Length);
        var nameBytes = Encoding.ASCII.GetBytes(name);
        // A local extra field aligns the uncompressed payload to a 64-byte boundary.
        var padding = (64 - (offset + 30L + nameBytes.Length + 4) % 64) % 64;
        zip.Write(0x04034b50u);
        WriteVersionAndFlags(zip);
        var crcOffset = stream.Position;
        zip.Write(0u);
        zip.Write(length);
        zip.Write(length);
        zip.Write((ushort)nameBytes.Length);
        zip.Write((ushort)(4 + padding));
        zip.Write(nameBytes);
        zip.Write((ushort)0x1986);
        zip.Write((ushort)padding);
        zip.Write(new byte[padding]);

        var buffer = new byte[64 * 1024];
        uint crc = uint.MaxValue;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            foreach (var value in buffer.AsSpan(0, count))
            {
                crc = crcTable[(crc ^ value) & 255] ^ (crc >> 8);
            }

            zip.Write(buffer.AsSpan(0, count));
        }

        crc = ~crc;
        var end = stream.Position;
        stream.Position = crcOffset;
        zip.Write(crc);
        stream.Position = end;
        return new UsdzEntry(nameBytes, offset, length, crc);
    }

    private static void WriteDirectoryEntry(BinaryWriter zip, UsdzEntry entry)
    {
        zip.Write(0x02014b50u);
        zip.Write((ushort)20);
        WriteVersionAndFlags(zip);
        zip.Write(entry.Crc);
        zip.Write(entry.Length);
        zip.Write(entry.Length);
        zip.Write((ushort)entry.Name.Length);
        zip.Write(new byte[8]); // Extra length, comment length, disk and internal attributes.
        zip.Write(0u); // External attributes.
        zip.Write(entry.Offset);
        zip.Write(entry.Name);
    }

    private static void WriteVersionAndFlags(BinaryWriter zip)
    {
        zip.Write((ushort)20);
        zip.Write((ushort)0); // Flags.
        zip.Write((ushort)0); // Stored, without compression.
        zip.Write((ushort)0); // Time.
        zip.Write((ushort)33); // 1980-01-01.
    }

    private static uint ZipOffset(long value)
    {
        if (value >= uint.MaxValue)
        {
            throw new EchoException("The USDZ package exceeds the 4 GB export limit.", 2);
        }

        return (uint)value;
    }

    private static uint[] CrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xedb88320 : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
