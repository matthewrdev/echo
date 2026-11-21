using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Echo.Models;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using Echo.Telemetry;

namespace Echo.Validation;

public sealed class ValidationApplication
{
    private readonly IValidationInputs inputs;
    private readonly IReconstructionMeasurements measurements;
    private readonly IProcessor processor;
    private readonly IProjectStoreFactory stores;
    private readonly IJsonCodec json;
    private readonly IFileOperations files;
    private readonly IPerformanceTraceStore traces;

    public ValidationApplication(IValidationInputs inputs, IReconstructionMeasurements measurements, IProcessor processor,
        IProjectStoreFactory stores, IJsonCodec json, IFileOperations files, IPerformanceTraceStore traces)
    {
        this.inputs = inputs;
        this.measurements = measurements;
        this.processor = processor;
        this.stores = stores;
        this.json = json;
        this.files = files;
        this.traces = traces;
    }

    public int Run(string[] args, CancellationToken cancellation)
    {
        if (args.Length == 4 && args[0] is "measure" or "topology")
        {
            var store = stores.Open(args[1]);
            var run = store.GetRun(args[2]);
            if (args[0] == "topology")
            {
                var report = measurements.Topology(store, run, cancellation);
                using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write);
                json.Write(output, report);
            }
            else
            {
                var report = measurements.Read(store, run, cancellation);
                using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write);
                json.Write(output, report);
            }

            return 0;
        }

        if (args.Length < 4 || args[0] != "run" || args.Length % 2 != 0)
        {
            Console.Error.WriteLine("Usage: Echo.Validation run CASE.json ASSETS OUTPUT [--memory-mib N] [--max-seconds N] [--max-peak-mib N]\n       Echo.Validation measure|topology PROJECT RUN OUTPUT.json");
            return 2;
        }

        var fixture = json.Read<ReconstructionCase>(args[1]);
        QualityGate.Validate(fixture.Limits);
        var options = ReadOptions(args, fixture.Settings);
        fixture.Settings.Validate();
        var outputRoot = Path.GetFullPath(args[3]);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
        {
            throw new IOException("Output already exists. Use a new directory to guarantee a fresh reconstruction.");
        }

        Directory.CreateDirectory(outputRoot);
        var result = CreateResult(fixture, args[1], options);
        var reportPath = Path.Combine(outputRoot, "validation.json");
        files.AtomicJson(reportPath, result);
        using var process = Process.GetCurrentProcess();
        var watch = Stopwatch.StartNew();
        var processingStarted = false;
        IProjectStore? prepared = null;
        try
        {
            var store = inputs.Prepare(fixture, args[2], outputRoot, cancellation);
            prepared = store;
            result.PreparationSeconds = watch.Elapsed.TotalSeconds;
            files.AtomicJson(Path.Combine(outputRoot, "case.json"), fixture);
            Console.WriteLine($"Processing {fixture.Name}: {fixture.PhotoHashes.Count} pinned photographs, fresh checkpoints.");
            watch.Restart();
            processingStarted = true;
            RunData run;
            var eventOptions = new System.Text.Json.JsonSerializerOptions(json.Options) { WriteIndented = false };
            using (var events = new StreamWriter(Path.Combine(outputRoot, "events.ndjson")))
            {
                run = processor.Execute(store, "export", cancellation, output: value =>
                {
                    events.WriteLine(System.Text.Json.JsonSerializer.Serialize(value, eventOptions));
                });
            }

            result.ProcessingSeconds = watch.Elapsed.TotalSeconds;
            process.Refresh();
            result.PeakProcessWorkingSetBytes = AvailablePeak(process.PeakWorkingSet64);
            result.PeakRegisteredWorkingSetBytes = AvailablePeak(run.Progress?.Resources?.PeakWorkingSetBytes ?? 0);
            ReadMeasurements(result, fixture, store, run, cancellation);
            return Complete(result, reportPath);
        }
        catch (Exception error)
        {
            result.Status = error is OperationCanceledException ? "cancelled" : "failed";
            result.Error = error.Message;
            if (processingStarted && result.ProcessingSeconds == 0)
            {
                result.ProcessingSeconds = watch.Elapsed.TotalSeconds;
            }

            process.Refresh();
            result.PeakProcessWorkingSetBytes = AvailablePeak(Math.Max(result.PeakProcessWorkingSetBytes ?? 0, process.PeakWorkingSet64));
            ReadFailureSummary(result, prepared);
            throw;
        }
        finally
        {
            files.AtomicJson(reportPath, result);
        }
    }

    private Dictionary<string, double> ReadOptions(string[] args, Settings settings)
    {
        var options = new Dictionary<string, double>();
        for (var index = 4; index < args.Length; index += 2)
        {
            var name = args[index];
            var supported = name is "--memory-mib" or "--max-seconds" or "--max-peak-mib";
            var validValue = double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value) && value > 0;
            if (!supported || !validValue || !options.TryAdd(name, value))
            {
                throw new ArgumentException("Unknown, repeated or invalid validation option: " + name);
            }
        }

        if (options.TryGetValue("--memory-mib", out var memory))
        {
            if (memory != Math.Truncate(memory) || memory is < 256 or > 524287)
            {
                throw new ArgumentException("--memory-mib must be a whole number from 256 to 524287.");
            }

            settings.ProcessingMemoryBudgetMb = (int)memory;
            settings.DepthMemoryBudgetMb = 0;
            settings.FusionMemoryBudgetMb = 0;
            settings.DepthConcurrency = 0;
        }

        return options;
    }

    private ValidationResult CreateResult(ReconstructionCase fixture, string fixturePath, IReadOnlyDictionary<string, double> options)
    {
        return new ValidationResult
        {
            Case = fixture.Name,
            CaseSha256 = files.HashFile(fixturePath),
            Settings = fixture.Settings,
            Version = typeof(ValidationApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            BinarySha256 = Directory.EnumerateFiles(AppContext.BaseDirectory).Where(IsRuntimeBinary)
                .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetFileName(path), files.HashFile),
            MaximumSeconds = options.TryGetValue("--max-seconds", out var seconds) ? seconds : null,
            MaximumPeakWorkingSetBytes = options.TryGetValue("--max-peak-mib", out var peak) ? checked((long)(peak * 1048576)) : null
        };
    }

    private void ReadMeasurements(ValidationResult result, ReconstructionCase fixture, IProjectStore store, RunData run, CancellationToken cancellation)
    {
        result.RunId = run.Id;
        var directory = store.PathFor("runs/" + run.Id);
        var trace = traces.List(directory).LastOrDefault();
        if (trace != null)
        {
            foreach (var span in traces.Read(directory, trace.Id).Spans.Where(span => span.Kind == PerformanceSpanKind.Stage))
            {
                result.StageSeconds[span.Stage] = span.DurationMilliseconds / 1000;
            }
        }

        result.Checks = QualityGate.Evaluate(measurements.Read(store, run, cancellation), fixture.Limits);
        result.ExportSha256 = files.HashFile(store.PathFor(run.Artifacts["export"] + "/cliff.glb"));
    }

    private int Complete(ValidationResult result, string reportPath)
    {
        var qualityPassed = result.Checks.All(check => check.Passed);
        var timePassed = result.MaximumSeconds == null || result.ProcessingSeconds <= result.MaximumSeconds;
        var memoryPassed = result.PeakWorkingSetBytes is { } measuredPeak
            && (result.MaximumPeakWorkingSetBytes == null || measuredPeak <= result.MaximumPeakWorkingSetBytes);
        result.Status = qualityPassed && timePassed && memoryPassed ? "passed" : "failed";
        foreach (var check in result.Checks.Where(check => !check.Passed))
        {
            Console.Error.WriteLine($"FAIL {check.Metric}: {check.Actual} outside [{check.Minimum}, {check.Maximum}]");
        }

        if (!timePassed || !memoryPassed)
        {
            Console.Error.WriteLine("FAIL performance limit or missing memory measurement; see validation.json.");
        }

        Console.WriteLine($"{result.Status}: {result.ProcessingSeconds:F1}s, peak measured memory {result.PeakWorkingSetBytes / 1073741824.0:F2} GiB. {reportPath}");
        return result.Status == "passed" ? 0 : 1;
    }

    private void ReadFailureSummary(ValidationResult result, IProjectStore? prepared)
    {
        if (prepared != null && Directory.Exists(prepared.PathFor("runs")))
        {
            try
            {
                if (Directory.EnumerateFiles(prepared.PathFor("runs"), "run.json", SearchOption.AllDirectories).Any())
                {
                    var failedRun = prepared.GetRun();
                    result.RunId = failedRun.Id;
                    result.PeakRegisteredWorkingSetBytes = AvailablePeak(failedRun.Progress?.Resources?.PeakWorkingSetBytes ?? 0);
                }
            }
            catch (Exception recoveryError)
            {
                result.Error += " Run summary could not be read: " + recoveryError.Message;
            }
        }
    }

    private long? AvailablePeak(long bytes) => bytes > 0 ? bytes : null;

    private bool IsRuntimeBinary(string path)
    {
        var name = Path.GetFileName(path);
        var managed = name.StartsWith("Echo.", StringComparison.Ordinal) && Path.GetExtension(path) == ".dll";
        var native = Path.GetExtension(path) == ".dylib" || name == "echo-poisson";
        return managed || native;
    }
}
