using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Echo.Models;

namespace Echo.Export;

public sealed class PdalSurveyCloudWriter : ISurveyCloudWriter
{
    public SurveyExportCapabilities Inspect()
    {
        try
        {
            var stages = Run("--drivers").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0])
                .ToHashSet(StringComparer.Ordinal);
            var las = stages.Contains("writers.las");
            var canReproject = las && stages.Contains("filters.reprojection") && stages.Contains("filters.ferry");
            return new SurveyExportCapabilities(las, stages.Contains("writers.copc"), stages.Contains("writers.e57"), canReproject);
        }
        catch (EchoException error)
        {
            return new SurveyExportCapabilities(false, false, false, false, error.Message);
        }
    }

    public void CheckAvailable(PointCloudExportFormat format, bool projected)
    {
        var capabilities = Inspect();
        if (capabilities.Error != null)
        {
            throw new EchoException(capabilities.Error, 2);
        }

        var supported = format switch
        {
            PointCloudExportFormat.Laz => capabilities.Laz,
            PointCloudExportFormat.Copc => capabilities.Copc,
            PointCloudExportFormat.E57 => capabilities.E57,
            _ => capabilities.ProjectedCrs
        };
        if (!supported)
        {
            throw new EchoException($"This PDAL installation is missing support for {format}. Check echo doctor or choose another PDAL with ECHO_PDAL.", 2);
        }

        if (projected && !capabilities.ProjectedCrs)
        {
            throw new EchoException("This PDAL installation is missing CRS reprojection support.", 2);
        }
    }

    public void Write(string source, string destination, PointCloudExportFormat format, string? projectedCrs)
    {
        var outputType = format switch
        {
            PointCloudExportFormat.Copc => "writers.copc",
            PointCloudExportFormat.E57 => "writers.e57",
            _ => "writers.las"
        };

        var stages = new List<object> { new { type = "readers.las", filename = source } };
        if (projectedCrs != null)
        {
            stages.Add(Reprojection("EPSG:4978", "EPSG:4979"));
            stages.Add(new
            {
                type = "filters.ferry",
                dimensions = "Z=>EchoEllipsoidHeight"
            });
            stages.Add(Reprojection("EPSG:4979", projectedCrs));
            stages.Add(new
            {
                type = "filters.ferry",
                dimensions = "EchoEllipsoidHeight=>Z"
            });
        }

        stages.Add(WriterOptions(outputType, destination, format, projectedCrs != null));
        var pipeline = destination + ".pipeline.json";
        File.WriteAllText(pipeline, JsonSerializer.Serialize(stages));
        try
        {
            Run("pipeline", pipeline);
            if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
            {
                throw new EchoException("PDAL did not produce a point-cloud file.", 2);
            }

            if (projectedCrs != null)
            {
                VerifyProjectedCrs(destination, projectedCrs);
            }
        }
        finally
        {
            File.Delete(pipeline);
        }
    }

    private object Reprojection(string source, string destination) => new
    {
        type = "filters.reprojection",
        in_srs = source,
        out_srs = destination,
        error_on_failure = true
    };

    private Dictionary<string, object> WriterOptions(string type, string path, PointCloudExportFormat format, bool projected)
    {
        var options = new Dictionary<string, object> { ["type"] = type, ["filename"] = path };
        if (format == PointCloudExportFormat.E57)
        {
            options["double_precision"] = true;
            return options;
        }

        if (type == "writers.las")
        {
            options["compression"] = format == PointCloudExportFormat.Laz;
            options["software_id"] = "Echo";
            if (!projected)
            {
                options["forward"] = "all";
                return options;
            }

            options["minor_version"] = 4;
            options["dataformat_id"] = 7;
        }
        else if (!projected)
        {
            options["forward"] = "scale,offset";
            return options;
        }

        foreach (var axis in new[] { "x", "y", "z" })
        {
            options["scale_" + axis] = 0.001;
            options["offset_" + axis] = "auto";
        }

        return options;
    }

    private void VerifyProjectedCrs(string path, string crs)
    {
        using var information = JsonDocument.Parse(Run("info", "--metadata", path));
        var metadata = information.RootElement.GetProperty("metadata");
        var horizontalOnly = metadata.TryGetProperty("srs", out var srs)
            && srs.TryGetProperty("horizontal", out var horizontal)
            && IsProjection(horizontal.GetString());
        var hasVerticalCrs = srs.ValueKind == JsonValueKind.Object && srs.TryGetProperty("vertical", out var vertical)
            && !string.IsNullOrWhiteSpace(vertical.GetString());
        if (!horizontalOnly || hasVerticalCrs)
        {
            throw new EchoException($"{crs} must be a horizontal projected coordinate system. Use EPSG:4978 for Earth-centred XYZ.", 2);
        }
    }

    private bool IsProjection(string? value)
    {
        return value != null && (value.StartsWith("PROJCS[", StringComparison.Ordinal)
            || value.StartsWith("PROJCRS[", StringComparison.Ordinal));
    }

    private string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Executable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["PROJ_NETWORK"] = "OFF";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new EchoException("Could not start PDAL.", 2);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var output = stdout.GetAwaiter().GetResult();
            var errors = stderr.GetAwaiter().GetResult().Trim();
            if (process.ExitCode == 0)
            {
                return output;
            }

            var details = errors.Length == 0 ? output.Trim() : errors;
            if (arguments[0] == "--drivers")
            {
                var firstLine = details.Split('\n')[0];
                throw new EchoException($"PDAL could not start. Check the local installation or set ECHO_PDAL. {firstLine}", 2);
            }

            throw new EchoException($"PDAL {arguments[0]} failed. {details}", 2);
        }
        catch (Win32Exception)
        {
            throw new EchoException("This export needs PDAL. Install PDAL or set ECHO_PDAL to its executable path. E57 also needs the PDAL E57 plugin.", 2);
        }
    }

    private string Executable()
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_PDAL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var installed in new[] { "/opt/homebrew/bin/pdal", "/usr/local/bin/pdal" })
            {
                if (File.Exists(installed))
                {
                    return installed;
                }
            }
        }

        return "pdal";
    }
}
