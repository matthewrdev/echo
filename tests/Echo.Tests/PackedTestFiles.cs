using System.Text.Json;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Storage.Artifacts;
using Echo.Storage.Journals;

namespace Echo.Tests;

internal static class PackedTestFiles
{
    public static IEnumerable<JsonElement> ReadEvents(string path)
    {
        var reader = new BinaryJournalReader<RunEvent>(new RunEventRecordCodec());
        using var stream = reader.OpenRead(path) ?? throw new FileNotFoundException(null, path);
        foreach (var record in reader.Read(stream))
        {
            yield return JsonSerializer.SerializeToElement(record, CoreJsonReadContext.Default.RunEvent);
        }
    }

    public static DenseCloudReport ReadCloud(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        return new DenseCloudReportCodec().Read(reader);
    }

    public static void WriteCloud(string path, DenseCloudReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        new DenseCloudReportCodec().Write(writer, report);
    }

    public static void WriteTornFrame(string path)
    {
        using var writer = new BinaryWriter(File.Open(path, FileMode.Append));
        writer.Write(100);
        writer.Write((byte)0);
    }
}
