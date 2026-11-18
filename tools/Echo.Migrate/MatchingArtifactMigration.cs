using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Migrate;

[Export]
public sealed class MatchingArtifactMigration
{
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IFeatureArtifactStore features;
    private readonly IMatchArtifactStore matches;

    [ImportingConstructor]
    public MatchingArtifactMigration(IFileOperations files, IJsonCodec json, IFeatureArtifactStore features, IMatchArtifactStore matches)
    {
        this.files = files;
        this.json = json;
        this.features = features;
        this.matches = matches;
    }

    public bool Convert(string path, bool feature)
    {
        var directory = Path.GetDirectoryName(path)!;
        var transaction = Path.Combine(directory, ".binary-migration");
        if (Directory.Exists(transaction))
        {
            Complete(directory, transaction);
        }
        using (var input = File.OpenRead(path))
        {
            Span<byte> prefix = stackalloc byte[80];
            var count = input.Read(prefix);
            if (System.Text.Encoding.UTF8.GetString(prefix[..count]).Contains("\"storage\":\"echo-", StringComparison.Ordinal))
            {
                VerifyManifest(directory);
                return false;
            }
        }
        VerifyManifest(directory);
        var prepared = transaction + ".preparing";
        if (Directory.Exists(prepared))
        {
            Directory.Delete(prepared, true);
        }
        Directory.CreateDirectory(prepared);
        try
        {
            var target = Path.Combine(prepared, Path.GetFileName(path));
            if (feature)
            {
                using var input = File.OpenRead(path);
                var original = JsonSerializer.Deserialize(input, CoreJsonReadContext.Default.FeatureSet)
                    ?? throw new InvalidDataException("Empty legacy feature artifact.");
                features.Write(target, original);
                if (files.Hash(original) != files.Hash(features.Read(target)))
                {
                    throw new InvalidDataException("Feature migration changed numeric values or ordering.");
                }
            }
            else
            {
                using var input = File.OpenRead(path);
                var original = JsonSerializer.Deserialize(input, CoreJsonReadContext.Default.ImagePair)
                    ?? throw new InvalidDataException("Empty legacy matching artifact.");
                matches.Write(target, original);
                if (files.Hash(original) != files.Hash(matches.Read(target)))
                {
                    throw new InvalidDataException("Matching migration changed correspondences or verification.");
                }
            }
            var manifest = json.Read<Artifact>(Path.Combine(directory, "manifest.json"));
            foreach (var file in Directory.GetFiles(prepared))
            {
                manifest.Files[Path.GetFileName(file)] = files.HashFile(file);
            }
            files.AtomicJson(Path.Combine(prepared, "manifest.json"), manifest);
            Directory.Move(prepared, transaction);
            Complete(directory, transaction);
            return true;
        }
        finally
        {
            if (Directory.Exists(prepared))
            {
                Directory.Delete(prepared, true);
            }
        }
    }

    public void VerifyManifest(string directory)
    {
        var manifest = json.Read<Artifact>(Path.Combine(directory, "manifest.json"));
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(directory, file.Key);
            if (!File.Exists(path) || files.HashFile(path) != file.Value)
            {
                throw new InvalidDataException("Corrupt artifact before/after migration: " + path);
            }
        }
    }

    private void Complete(string directory, string transaction)
    {
        var manifest = json.Read<Artifact>(Path.Combine(transaction, "manifest.json"));
        foreach (var entry in manifest.Files)
        {
            var prepared = Path.Combine(transaction, entry.Key);
            var source = File.Exists(prepared) ? prepared : Path.Combine(directory, entry.Key);
            if (!File.Exists(source) || files.HashFile(source) != entry.Value)
            {
                throw new InvalidDataException("Corrupt prepared migration payload: " + source);
            }
        }
        // Prepared files have already been flushed. Rename on the same filesystem
        // rather than copying and flushing their contents again. The retained manifest
        // verifies either location when recovering an interrupted installation.
        foreach (var source in Directory.GetFiles(transaction).OrderBy(path =>
            Path.GetExtension(path) == ".bin" ? 0 : Path.GetFileName(path) == "manifest.json" ? 2 : 1))
        {
            var destination = Path.Combine(directory, Path.GetFileName(source));
            if (Path.GetFileName(source) != "manifest.json")
            {
                File.Move(source, destination, true);
                continue;
            }
            var temporary = destination + ".migration.tmp";
            File.Copy(source, temporary, true);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        VerifyManifest(directory);
        foreach (var name in manifest.Files.Keys)
        {
            File.Delete(Path.Combine(directory, name) + ".migration.tmp");
        }
        Directory.Delete(transaction, true);
    }
}
