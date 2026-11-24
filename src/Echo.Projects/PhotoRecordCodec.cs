using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Projects;

internal sealed class PhotoRecordCodec
{
    public Photo Read(BinaryReader reader)
    {
        var photo = new Photo
        {
            Id = reader.ReadString(),
            Name = reader.ReadString(),
            Source = reader.ReadString(),
            Preview = reader.ReadString(),
            Hash = reader.ReadString(),
            SourceHash = reader.ReadString(),
            Width = reader.ReadInt32(),
            Height = reader.ReadInt32(),
            Camera = reader.ReadString(),
            CapturedAt = reader.ReadString(),
            FocalPixels = reader.ReadDouble(),
            FocalSource = reader.ReadString(),
            Enabled = reader.ReadBoolean(),
            Discarded = reader.ReadBoolean(),
            DuplicateOf = reader.ReadOptionalString(),
            KeepWhenTrimming = reader.ReadBoolean()
        };

        photo.Gps = reader.ReadBoolean() ? ProjectModelCodec.ReadGeoPosition(reader) : null;
        photo.Attitude = reader.ReadBoolean() ? ProjectModelCodec.ReadAttitude(reader) : null;

        var sets = reader.ReadCount();
        for (var index = 0; index < sets; index++)
        {
            photo.Sets.Add(reader.ReadString());
        }

        photo.Metadata = reader.ReadJsonFields();
        return photo;
    }

    public void Write(BinaryWriter writer, Photo photo)
    {
        writer.Write(photo.Id);
        writer.Write(photo.Name);
        writer.Write(photo.Source);
        writer.Write(photo.Preview);
        writer.Write(photo.Hash);
        writer.Write(photo.SourceHash);
        writer.Write(photo.Width);
        writer.Write(photo.Height);
        writer.Write(photo.Camera);
        writer.Write(photo.CapturedAt);
        writer.Write(photo.FocalPixels);
        writer.Write(photo.FocalSource);
        writer.Write(photo.Enabled);
        writer.Write(photo.Discarded);
        writer.WriteOptionalString(photo.DuplicateOf);
        writer.Write(photo.KeepWhenTrimming);
        writer.Write(photo.Gps != null);
        if (photo.Gps is { } gps)
        {
            ProjectModelCodec.WriteGeoPosition(writer, gps);
        }

        writer.Write(photo.Attitude != null);
        if (photo.Attitude is { } attitude)
        {
            ProjectModelCodec.WriteAttitude(writer, attitude);
        }

        writer.Write(photo.Sets.Count);
        foreach (var set in photo.Sets)
        {
            writer.Write(set);
        }

        writer.Write(photo.Metadata.Count);
        foreach (var pair in photo.Metadata)
        {
            writer.Write(pair.Key);
            writer.WriteJsonValue(pair.Value);
        }
    }
}
