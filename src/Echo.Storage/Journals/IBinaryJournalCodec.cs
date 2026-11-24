namespace Echo.Storage.Journals;

public interface IBinaryJournalCodec<T>
{
    uint Magic
    {
        get;
    }
    T Read(BinaryReader reader);
    void Write(BinaryWriter writer, T value);
}
