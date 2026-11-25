using System.Text;
using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Projects;

public sealed class ProjectSnapshotCodec
{
    private const uint Magic = 0x31525045; // EPR1
    private const uint GpsPolygonMagic = 0x32525045; // EPR2
    private const uint CameraAccuracyMagic = 0x33525045; // EPR3
    private readonly PhotoRecordCodec photos = new();

    public ProjectData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadUInt32();
        if (magic is not (Magic or GpsPolygonMagic or CameraAccuracyMagic))
        {
            throw new InvalidDataException("Unsupported project snapshot.");
        }

        var project = new ProjectData
        {
            SchemaVersion = reader.ReadInt32(),
            Id = reader.ReadString(),
            Name = reader.ReadString(),
            Revision = reader.ReadInt32(),
            Settings = ProjectModelCodec.ReadSettings(reader),
            Images = reader.ReadArray(() => photos.Read(reader)).ToList(),
            Hints = reader.ReadArray(() => ProjectModelCodec.ReadHint(reader)).ToList()
        };
        var stages = reader.ReadCount();
        for (var index = 0; index < stages; index++)
        {
            project.StageRevisions.Add(reader.ReadString(), reader.ReadString());
        }

        if (magic is GpsPolygonMagic or CameraAccuracyMagic)
        {
            project.Settings.GpsPolygons = GpsPolygonCodec.Read(reader);
        }

        if (magic == CameraAccuracyMagic)
        {
            var count = reader.ReadCount();
            var hints = project.Hints.ToDictionary(hint => hint.Id, StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var id = reader.ReadString();
                hints[id].VerticalUncertaintyMetres = reader.ReadDouble();
            }
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Unexpected project snapshot data.");
        }

        return project;
    }

    public void Write(Stream stream, ProjectData project)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var polygons = project.Settings.GpsPolygons;
        var hasPolygons = polygons is { Count: > 0 };
        var accuracyHints = project.Hints.Where(hint => hint.VerticalUncertaintyMetres.HasValue).ToArray();
        writer.Write(accuracyHints.Length > 0 ? CameraAccuracyMagic : hasPolygons ? GpsPolygonMagic : Magic);
        writer.Write(project.SchemaVersion);
        writer.Write(project.Id);
        writer.Write(project.Name);
        writer.Write(project.Revision);
        ProjectModelCodec.WriteSettings(writer, project.Settings);
        writer.WriteCollection(project.Images, photo => photos.Write(writer, photo));
        writer.WriteCollection(project.Hints, hint => ProjectModelCodec.WriteHint(writer, hint));
        writer.Write(project.StageRevisions.Count);
        foreach (var pair in project.StageRevisions)
        {
            writer.Write(pair.Key);
            writer.Write(pair.Value);
        }

        if (hasPolygons || accuracyHints.Length > 0)
        {
            GpsPolygonCodec.Write(writer, polygons ?? []);
        }

        if (accuracyHints.Length > 0)
        {
            writer.WriteCollection(accuracyHints, hint =>
            {
                writer.Write(hint.Id);
                writer.Write(hint.VerticalUncertaintyMetres!.Value);
            });
        }
    }
}
