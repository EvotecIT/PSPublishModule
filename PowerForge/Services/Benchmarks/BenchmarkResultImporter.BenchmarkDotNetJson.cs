using System.Text.Json;

namespace PowerForge;

public sealed partial class BenchmarkResultImporter
{
    // JsonExporter writes descriptor + ": " + Job.DisplayInfo + " " + Parameters.DisplayInfo.
    // MethodTitle can itself contain colons; parameter values can contain spaces and brackets.
    private static string? GetBenchmarkDotNetDisplayJob(JsonElement benchmark)
    {
        string? display = GetString(benchmark, "DisplayInfo");
        string? title = GetString(benchmark, "MethodTitle") ?? GetString(benchmark, "Method");
        if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(title))
            return null;

        string marker = "." + title + ": ";
        int markerIndex = display!.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
            return null;

        string job = display.Substring(markerIndex + marker.Length);
        string? parameters = GetString(benchmark, "Parameters");
        if (!string.IsNullOrEmpty(parameters))
        {
            if (!job.EndsWith("]", StringComparison.Ordinal))
                return null;
            int suffixStart = job.IndexOf(" [", StringComparison.Ordinal);
            while (suffixStart >= 0)
            {
                if (MatchesBenchmarkDotNetDisplayParameters(job, suffixStart + 2, parameters!))
                    return suffixStart == 0 ? null : job.Substring(0, suffixStart);
                suffixStart = job.IndexOf(" [", suffixStart + 2, StringComparison.Ordinal);
            }
            return null;
        }

        return string.IsNullOrWhiteSpace(job) ? null : job;
    }

    private static bool MatchesBenchmarkDotNetDisplayParameters(string display, int start, string printInfo)
    {
        // PrintInfo joins fields with '&'; DisplayInfo uses ', '. Literal '&' in a value stays '&'.
        int position = start;
        foreach (char character in printInfo)
        {
            if (position >= display.Length - 1)
                return false;
            if (display[position] == character)
                position++;
            else if (character == '&' && display[position] == ',' && position + 1 < display.Length - 1 && display[position + 1] == ' ')
                position += 2;
            else
                return false;
        }
        return position == display.Length - 1;
    }

    private static bool IsBenchmarkDotNetParameterStart(string text, int start)
    {
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        if (start >= text.Length || !(char.IsLetter(text[start]) || text[start] == '_'))
            return false;
        start++;
        while (start < text.Length && (char.IsLetterOrDigit(text[start]) || text[start] == '_')) start++;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        return start < text.Length && text[start] is '=' or ':';
    }

    private static bool TryImportBenchmarkDotNetJson(JsonElement root, string path, string? suite, out BenchmarkRunResult result)
    {
        result = new BenchmarkRunResult();
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(root, "Benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
            return false;

        var environment = GetBenchmarkDotNetEnvironment(root);
        var samples = new List<BenchmarkSample>();
        foreach (var benchmark in benchmarks.EnumerateArray())
        {
            if (benchmark.ValueKind != JsonValueKind.Object)
                continue;

            var method = GetString(benchmark, "Method")
                         ?? GetString(benchmark, "MethodTitle")
                         ?? GetString(benchmark, "FullName")
                         ?? GetString(benchmark, "DisplayInfo")
                         ?? Path.GetFileNameWithoutExtension(path);
            var statistics = TryGetObject(benchmark, "Statistics");
            var mean = GetDouble(statistics, "Median") ?? GetDouble(statistics, "Mean");
            if (mean.HasValue)
                mean *= 0.000001;

            var displayJob = GetBenchmarkDotNetDisplayJob(benchmark);
            var variables = ParseBenchmarkDotNetParameters(GetString(benchmark, "Parameters"), usePrintInfo: displayJob is not null);
            AddBenchmarkDotNetIdentityVariables(benchmark, variables, method);
            var engine = GetBenchmarkDotNetEngine(benchmark, displayJob);
            var metrics = ExtractBenchmarkDotNetMetrics(statistics);
            AddBenchmarkDotNetMemoryMetrics(TryGetObject(benchmark, "Memory"), metrics);
            double[] originalValues = GetBenchmarkDotNetOriginalValues(statistics);
            if (originalValues.Length > 0)
            {
                for (int index = 0; index < originalValues.Length; index++)
                {
                    samples.Add(CreateBenchmarkDotNetSample(
                        suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path),
                        method,
                        engine,
                        environment.OsFamily,
                        index,
                        originalValues[index] * 0.000001,
                        variables,
                        metrics));
                }
            }
            else
            {
                samples.Add(CreateBenchmarkDotNetSample(
                    suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path),
                    method,
                    engine,
                    environment.OsFamily,
                    0,
                    mean,
                    variables,
                    metrics));
            }
        }

        if (samples.Count == 0)
            return false;

        result = BuildImportedResult(suite ?? GetString(root, "Title") ?? Path.GetFileNameWithoutExtension(path), samples);
        result.Environment = environment;
        return true;
    }

    private static BenchmarkSample CreateBenchmarkDotNetSample(
        string suite,
        string method,
        string engine,
        string os,
        int iteration,
        double? durationMs,
        IDictionary<string, string?> variables,
        IDictionary<string, double> metrics)
        => new()
        {
            RunId = "import",
            Suite = suite,
            Scenario = method,
            Operation = "Run",
            Engine = engine,
            Host = string.Empty,
            Os = os,
            RunMode = "import",
            Iteration = iteration,
            Status = durationMs.HasValue
                ? BenchmarkSampleStatus.Succeeded
                : BenchmarkSampleStatus.Failed,
            DurationMs = durationMs ?? 0,
            Reason = durationMs.HasValue
                ? string.Empty
                : "BenchmarkDotNet JSON duration could not be parsed.",
            Variables = new Dictionary<string, string?>(
                variables,
                StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(
                metrics,
                StringComparer.OrdinalIgnoreCase)
        };

    private static double[] GetBenchmarkDotNetOriginalValues(JsonElement? statistics)
    {
        if (!statistics.HasValue ||
            statistics.Value.ValueKind != JsonValueKind.Object ||
            !BenchmarkJson.TryGetPropertyIgnoreCase(
                statistics.Value,
                "OriginalValues",
                out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<double>();
        }

        var result = new List<double>();
        foreach (JsonElement value in values.EnumerateArray())
        {
            double? number = GetDoubleValue(value);
            if (!number.HasValue ||
                number.Value < 0 ||
                double.IsNaN(number.Value) ||
                double.IsInfinity(number.Value))
            {
                return Array.Empty<double>();
            }
            result.Add(number.Value);
        }

        return result.ToArray();
    }

    private static string BenchmarkDotNetJsonReportFamily(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var reportIndex = name.IndexOf("-report", StringComparison.OrdinalIgnoreCase);
        return reportIndex < 0
            ? Path.Combine(directory, name)
            : Path.Combine(directory, name.Substring(0, reportIndex) + "-report");
    }

    private static int BenchmarkDotNetJsonReportPreference(string path)
    {
        var name = Path.GetFileName(path);
        if (name.IndexOf("full-compressed", StringComparison.OrdinalIgnoreCase) >= 0) return 30;
        if (name.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return 20;
        if (name.EndsWith("-report.json", StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    private static bool IsBenchmarkDotNetJsonReport(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && BenchmarkJson.TryGetPropertyIgnoreCase(doc.RootElement, "Benchmarks", out var benchmarks)
                   && benchmarks.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetBenchmarkDotNetEngine(JsonElement benchmark, string? displayJob)
    {
        var job = GetString(benchmark, "Job")
                  ?? GetString(benchmark, "JobDisplayInfo")
                  ?? GetString(benchmark, "JobId");
        if (!string.IsNullOrWhiteSpace(job))
            return job!;

        if (BenchmarkJson.TryGetPropertyIgnoreCase(benchmark, "Job", out var jobNode) && jobNode.ValueKind == JsonValueKind.Object)
        {
            var parts = new[]
            {
                GetString(jobNode, "DisplayInfo"),
                GetString(jobNode, "Id"),
                GetString(jobNode, "Runtime"),
                GetString(jobNode, "RuntimeMoniker"),
                GetString(jobNode, "Platform"),
                GetString(jobNode, "Jit")
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            if (parts.Length > 0)
                return string.Join("; ", parts);
        }

        return displayJob ?? "BenchmarkDotNet";
    }

    private static BenchmarkEnvironmentInfo GetBenchmarkDotNetEnvironment(JsonElement root)
    {
        if (!BenchmarkJson.TryGetPropertyIgnoreCase(root, "HostEnvironmentInfo", out var hostNode) || hostNode.ValueKind != JsonValueKind.Object)
            return new BenchmarkEnvironmentInfo();

        var osDescription = GetString(hostNode, "OsVersion")
                            ?? GetString(hostNode, "OperatingSystem")
                            ?? string.Empty;
        return new BenchmarkEnvironmentInfo
        {
            OsFamily = BenchmarkPlatformNormalizer.NormalizeFamily(osDescription),
            OsDescription = osDescription,
            OsArchitecture = GetString(hostNode, "OsArchitecture") ?? GetString(hostNode, "Architecture") ?? string.Empty,
            ProcessArchitecture = GetString(hostNode, "ProcessArchitecture") ?? GetString(hostNode, "Architecture") ?? string.Empty,
            ProcessorName = GetString(hostNode, "ProcessorName") ?? string.Empty,
            PhysicalProcessorCount = GetInt32(hostNode, "PhysicalProcessorCount"),
            PhysicalCoreCount = GetInt32(hostNode, "PhysicalCoreCount"),
            LogicalCoreCount = GetInt32(hostNode, "LogicalCoreCount"),
            RuntimeVersion = GetString(hostNode, "RuntimeVersion") ?? GetString(hostNode, "Runtime") ?? string.Empty,
            DotNetSdkVersion = GetString(hostNode, "DotNetCliVersion") ?? string.Empty,
            Runner = GetString(hostNode, "BenchmarkDotNetCaption")
                     ?? GetString(hostNode, "BenchmarkDotNetVersion")
                     ?? "BenchmarkDotNet"
        };
    }

}
