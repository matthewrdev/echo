using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Projects;

internal sealed class ProjectImportRecordCodec : IBinaryJournalCodec<ProjectImportRecord>
{
    private readonly PhotoRecordCodec photos = new();

    public uint Magic => 0x31495045; // EPI1

    public ProjectImportRecord Read(BinaryReader reader)
    {
        var revision = reader.ReadInt32();
        return reader.ReadBoolean()
            ? new ProjectImportRecord { Revision = revision, Photo = photos.Read(reader) }
            : new ProjectImportRecord { Revision = revision, PhotoId = reader.ReadString(), Set = reader.ReadString() };
    }

    public void Write(BinaryWriter writer, ProjectImportRecord value)
    {
        writer.Write(value.Revision);
        writer.Write(value.Photo != null);
        if (value.Photo is { } photo)
        {
            photos.Write(writer, photo);
        }
        else
        {
            writer.Write(value.PhotoId!);
            writer.Write(value.Set!);
        }
    }

}
