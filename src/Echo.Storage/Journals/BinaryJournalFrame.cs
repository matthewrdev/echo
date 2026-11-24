namespace Echo.Storage.Journals;

internal static class BinaryJournalFrame
{
    internal static void ReadHeader(BinaryReader reader, uint magic)
    {
        if (reader.ReadUInt32() != magic)
        {
            throw new InvalidDataException("Unsupported binary journal format.");
        }
    }

    internal static bool TryRead(BinaryReader reader, long length, out int size)
    {
        var stream = reader.BaseStream;
        var start = stream.Position;
        size = 0;
        if (length - start < sizeof(int))
        {
            return false;
        }

        size = reader.ReadInt32();
        ValidateSize(size);
        if (length - stream.Position < size)
        {
            stream.Position = start;
            return false;
        }

        return true;
    }

    internal static void ValidateSize(int size)
    {
        if (size <= 0 || size > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("Invalid binary journal record size.");
        }
    }
}
