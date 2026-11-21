using System.Text.Json;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Metadata;
using SkiaSharp;

namespace Echo.Tests;

public sealed class MetadataTests
{
    private readonly IMetadataReader reader;
    private readonly IMetadataPayloadReader payloads;

    public MetadataTests(IMetadataReader reader, IMetadataPayloadReader payloads)
    {
        this.reader = reader;
        this.payloads = payloads;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("iPhone rear-camera pose uses true north and normalized image axes", () =>
        {
            var metadata = new Dictionary<string, JsonElement>
            {
                ["Make"] = JsonSerializer.SerializeToElement("Apple"),
                ["Model"] = JsonSerializer.SerializeToElement("iPhone 16 Pro"),
                ["FrontFacingCamera"] = JsonSerializer.SerializeToElement(0),
                ["GPSImgDirectionRef"] = JsonSerializer.SerializeToElement("T"),
                ["GPSImgDirection"] = JsonSerializer.SerializeToElement(90)
            };
            var vectors = new Dictionary<int, double[]>
            {
                [1] = [-.8, -.36, .48],
                [3] = [.8, .36, .48],
                [6] = [.36, -.8, .48],
                [8] = [-.36, .8, .48]
            };

            foreach (var (orientation, vector) in vectors)
            {
                metadata["Orientation"] = JsonSerializer.SerializeToElement(orientation);
                metadata["AccelerationVector"] = JsonSerializer.SerializeToElement(vector);
                var attitude = IPhoneCapturePose.Estimate(metadata) ?? throw new InvalidOperationException("Expected an iPhone pose");
                var rotation = CameraGeometry.AttitudeRotation(attitude);
                Check(attitude.Source == "iPhone sensor estimate" && attitude.Yaw == 90, "East-facing capture estimate");
                Check(rotation[2] > .8 && Math.Abs(rotation[8]) < 1e-10, "Forward is east of the camera");
                Check(Math.Abs(rotation[3] + .36) < 1e-10 && Math.Abs(rotation[4] + .8) < 1e-10
                    && Math.Abs(rotation[5] - .48) < 1e-10, "World down projects correctly into the rotated image");
            }

            metadata["GPSImgDirectionRef"] = JsonSerializer.SerializeToElement("M");
            Check(IPhoneCapturePose.Estimate(metadata) == null, "Magnetic heading cannot be used as true north");
            metadata["GPSImgDirectionRef"] = JsonSerializer.SerializeToElement("T");
            metadata["FrontFacingCamera"] = JsonSerializer.SerializeToElement(1);
            Check(IPhoneCapturePose.Estimate(metadata) == null, "Front camera has different axes");
            metadata["FrontFacingCamera"] = JsonSerializer.SerializeToElement(0);
            metadata["AccelerationVector"] = JsonSerializer.SerializeToElement(new[] { 0, 0, 1 });
            Check(IPhoneCapturePose.Estimate(metadata) == null, "Near-vertical capture has no stable heading");
        });
        tests.Run("GPS altitude sign supports modern below-sea-level references", () =>
        {
            var gps = new MetadataExtractor.Formats.Exif.GpsDirectory();
            gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagAltitude, new MetadataExtractor.Rational(12, 1));
            gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagAltitudeRef, 3);
            var metadata = new PhotoMetadata();
            MetadataFields.Add(gps, metadata);
            Check(metadata.Fields["GPS:GPSAltitude"].GetDouble() == -12, "Reference 3 is negative");
            var exif = new MetadataExtractor.Formats.Exif.ExifSubIfdDirectory();
            exif.Set(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagExifVersion, "0300"u8.ToArray());
            MetadataFields.Add(exif, metadata);
            Check(metadata.Fields["ExifIFD:ExifVersion"].GetString() == "0300", "EXIF version remains available for reference inference");
        });
        tests.Run("Managed TIFF metadata preserves camera priors in both byte orders", () =>
        {
            foreach (var little in new[] { true, false })
            {
                WithFile(MetadataFixture.Tiff(little), path =>
                {
                    var fields = reader.Read(path);
                    Check(fields["Model"].GetString() == "FC8482", "Model");
                    Check(fields["DateTimeOriginal"].GetString() == "2026:03:07 07:11:43", "Capture date");
                    Check(fields["Orientation"].GetInt32() == 6, "EXIF orientation");
                    Check(fields["FocalLengthIn35mmFormat"].GetInt32() == 24, "35mm focal length");
                    Check(Math.Abs(fields["FocalLength"].GetDouble() - 6.72) < 1e-10, "Rational focal length");
                    Check(fields["GPSLatitude"].GetDouble() == -35.5, "Southern latitude");
                    Check(fields["GPSLongitude"].GetDouble() == -150.75, "Western longitude");
                    Check(Math.Abs(fields["GPSAltitude"].GetDouble() + 6.72) < 1e-10, "Below-sea altitude");
                });
            }
        });
        tests.Run("Managed JPEG metadata reads DJI XMP and exact APP7 binary payloads", () =>
        {
            WithFile(MetadataFixture.Jpeg(), path =>
            {
                var fields = reader.Read(path);
                Check(fields["AbsoluteAltitude"].GetDouble() == 87.202, "XMP altitude");
                Check(fields["GimbalPitchDegree"].GetDouble() == -13.4, "Gimbal attitude");
                Check(fields["CameraSerialNumber"].GetString() == "001234", "Identifiers retain leading zeroes");
                Check(fields["ImageWidth"].GetInt32() == 32 && fields["ImageHeight"].GetInt32() == 16, "Dimensions");
                var grouped = reader.Read(path, true);
                Check(grouped["DJI:Histogram"].GetString() == "(Binary data 6 bytes)", "Binary UI marker");
                Check(grouped.ContainsKey("XMP-drone-dji:GimbalYawDegree"), "XMP group preserved");
                Check(payloads.Read(path, "DJI:Histogram").AsSpan().SequenceEqual(new byte[] { 0, 255, 93, 1, 2, 0 }),
                    "Binary values preserve embedded brackets and trailing zeroes");
                Check(payloads.Read(path, "Histogram").Length == 6, "Unqualified payload lookup");
                Parallel.For(0, 8, _ => Check(reader.Read(path)["Model"].GetString() == "FC8482", "Concurrent reads are independent"));
            });
        });
        tests.Run("Managed metadata accepts PNG and reports malformed inputs", () =>
        {
            using var bitmap = new SKBitmap(16, 8);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            WithFile(data.ToArray(), path =>
            {
                var fields = reader.Read(path);
                Check(fields["ImageWidth"].GetInt32() == 16 && fields["ImageHeight"].GetInt32() == 8, "PNG dimensions");
            });
            WithFile([1, 2, 3, 4], path =>
            {
                try
                {
                    reader.Read(path);
                    throw new InvalidOperationException("Malformed file must produce a readable error");
                }
                catch (EchoException error)
                {
                    Check(error.Message.Contains("metadata"), "Readable error");
                }
            });
            var truncated = new PhotoMetadata();
            DjiDebugMetadata.Decode("DJI-DBG\0[hiso:missing-end"u8, truncated);
            Check(truncated.Payloads.Count == 0 && truncated.Fields.ContainsKey("DJI:Warning"), "Truncated vendor block is never exposed as valid data");
        });
    }

    private void WithFile(byte[] bytes, Action<string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, bytes);
            check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
