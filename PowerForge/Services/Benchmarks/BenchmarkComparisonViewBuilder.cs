using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Builds a presentation-ready view of a benchmark evidence catalog: for every comparison, every run with
/// all contenders and cases side by side, relative times, wins and the subject library against each competitor.
/// </summary>
/// <remarks>
/// <para>
/// Contenders are matched to the comparison's declared libraries by name, ignoring case and punctuation, so
/// "MagickNETEncode" belongs to "Magick.NET". When the same suffix follows two or more libraries ("Encode",
/// "WriteDataReader") it is an operation and becomes part of the case; a suffix used by one library only
/// ("WriteDataReaderParallel") names a variant of that library.
/// </para>
/// <para>
/// BenchmarkDotNet job characteristics are never case parameters, and parameters come from the benchmark's
/// full name when it has one. Results imported before the importer separated them are normalized here, so
/// published evidence does not need to be re-measured.
/// </para>
/// </remarks>
public sealed class BenchmarkComparisonViewBuilder
{
    private const double EvenTolerance = 0.03;
    private static readonly string[] PlatformOrder = { "windows", "linux", "macos" };

    /// <summary>Builds the view for a catalog file. Results are read beside the catalog.</summary>
    public BenchmarkComparisonCatalogView Build(string catalogPath)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new ArgumentException("Catalog path is required.", nameof(catalogPath));

        var fullPath = Path.GetFullPath(catalogPath);
        var catalog = BenchmarkJson.Read<BenchmarkEvidenceCatalog>(fullPath);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        return Build(catalog, entry =>
        {
            var file = ResolveResultFile(directory, entry);
            if (!File.Exists(file))
                throw new FileNotFoundException($"Benchmark result for '{entry.ComparisonId}' ({entry.Platform}, {entry.RunMode}) was not found.", file);
            return BenchmarkJson.Read<BenchmarkRunResult>(file);
        });
    }

    /// <summary>Builds the view for a catalog using the supplied result loader.</summary>
    public BenchmarkComparisonCatalogView Build(BenchmarkEvidenceCatalog catalog, Func<BenchmarkEvidenceEntry, BenchmarkRunResult> loadResult)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (loadResult is null) throw new ArgumentNullException(nameof(loadResult));

        var definitions = catalog.Comparisons.ToList();
        foreach (var entry in catalog.Entries)
        {
            if (definitions.All(definition => !string.Equals(definition.ComparisonId, entry.ComparisonId, StringComparison.OrdinalIgnoreCase)))
                definitions.Add(new BenchmarkComparisonDefinition { ComparisonId = entry.ComparisonId, Area = entry.Area, Title = entry.ComparisonId });
        }

        var comparisons = definitions.Select(definition =>
        {
            var entries = catalog.Entries
                .Where(entry => string.Equals(entry.ComparisonId, definition.ComparisonId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return BuildComparison(definition, entries.Select(entry => (entry, loadResult(entry))).ToArray());
        }).ToArray();

        return new BenchmarkComparisonCatalogView
        {
            Areas = catalog.Areas.Length > 0
                ? catalog.Areas.ToArray()
                : comparisons.Select(comparison => comparison.Area).Where(area => area.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Comparisons = comparisons
        };
    }

    private static string ResolveResultFile(string catalogDirectory, BenchmarkEvidenceEntry entry)
    {
        var relative = !string.IsNullOrWhiteSpace(entry.ArtifactRelativePath) ? entry.ArtifactRelativePath
            : !string.IsNullOrWhiteSpace(entry.ArtifactFileName) ? entry.ArtifactFileName
            : entry.ResultPath.Split('/', '\\').Last();
        return Path.Combine(catalogDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static BenchmarkComparisonView BuildComparison(
        BenchmarkComparisonDefinition definition,
        IReadOnlyList<(BenchmarkEvidenceEntry Entry, BenchmarkRunResult Result)> runs)
    {
        var libraries = definition.Libraries ?? Array.Empty<string>();
        var subject = libraries.FirstOrDefault() ?? string.Empty;
        var runViews = runs
            .OrderBy(run => PlatformRank(run.Entry.Platform))
            .ThenBy(run => string.Equals(run.Entry.RunMode, "full", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(run => run.Entry.GeneratedUtc)
            .Select(run => BuildRun(run.Entry, run.Result, libraries, subject))
            .ToArray();
        MakeRunIdsUnique(runViews);

        var preferred = runViews
            .OrderByDescending(run => (run.IsFull ? 4 : 0) + (run.Published ? 2 : 0) + (run.Platform == "windows" ? 1 : 0))
            .ThenByDescending(run => run.GeneratedUtc)
            .FirstOrDefault();

        return new BenchmarkComparisonView
        {
            Id = definition.ComparisonId,
            Area = definition.Area,
            Title = string.IsNullOrWhiteSpace(definition.Title) ? definition.ComparisonId : definition.Title,
            Libraries = libraries.ToArray(),
            Subject = subject,
            Measured = runViews.Length > 0,
            PreferredRunId = preferred?.Id ?? string.Empty,
            Runs = runViews,
            Overview = BuildOverview(runViews, preferred)
        };
    }

    private static int PlatformRank(string platform)
    {
        var index = Array.IndexOf(PlatformOrder, (platform ?? string.Empty).ToLowerInvariant());
        return index < 0 ? PlatformOrder.Length : index;
    }

    private static void MakeRunIdsUnique(BenchmarkRunView[] runs)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in runs)
        {
            seen[run.Id] = seen.TryGetValue(run.Id, out var count) ? count + 1 : 1;
            if (seen[run.Id] > 1)
                run.Id = run.Id + "-" + seen[run.Id].ToString(CultureInfo.InvariantCulture);
        }
    }

    private static BenchmarkContenderAcrossRunsView[] BuildOverview(BenchmarkRunView[] runs, BenchmarkRunView? preferred)
    {
        var names = new List<string>();
        foreach (var run in runs)
            foreach (var contender in run.Contenders)
                if (!names.Contains(contender.Name, StringComparer.Ordinal))
                    names.Add(contender.Name);

        double OrderKey(string name)
            => preferred?.Contenders.FirstOrDefault(c => c.Name == name)?.Relative.Ratio ?? double.MaxValue;

        return names
            .Select(name => new
            {
                Name = name,
                IsSubject = runs.SelectMany(run => run.Contenders).Any(c => c.Name == name && c.IsSubject)
            })
            .OrderBy(item => item.IsSubject ? 0 : 1)
            .ThenBy(item => OrderKey(item.Name))
            .Select(item => new BenchmarkContenderAcrossRunsView
            {
                Name = item.Name,
                IsSubject = item.IsSubject,
                Runs = runs.Select(run => run.Contenders.FirstOrDefault(c => c.Name == item.Name)?.Relative ?? new BenchmarkRatioView()).ToArray()
            })
            .ToArray();
    }

    private static BenchmarkRunView BuildRun(BenchmarkEvidenceEntry entry, BenchmarkRunResult result, string[] libraries, string subject)
    {
        var rows = NormalizeRows(result.Summary ?? Array.Empty<BenchmarkSummaryRow>(), libraries, subject);
        var statistic = rows.Any(row => row.Statistic == "mean") ? "mean" : "median";

        // A parameter present with the same value in every row describes the workload; one that differs or
        // is missing from any row identifies cases. Identity-only values (job, recorded operation) are not
        // shown as workload facts when they are constant.
        var parameterValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var parameterRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parameterOrder = new List<string>();
        var identityOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var parameter in row.Parameters.Concat(row.Identity))
            {
                if (!parameterValues.TryGetValue(parameter.Key, out var values))
                {
                    parameterValues[parameter.Key] = values = new HashSet<string>(StringComparer.Ordinal);
                    parameterOrder.Add(parameter.Key);
                }
                values.Add(parameter.Value);
                parameterRows[parameter.Key] = parameterRows.TryGetValue(parameter.Key, out var count) ? count + 1 : 1;
            }
            foreach (var key in row.Identity.Keys)
                identityOnly.Add(key);
        }
        bool IsConstant(string name) => parameterValues[name].Count == 1 && parameterRows[name] == rows.Count;
        var varying = parameterOrder.Where(name => !IsConstant(name)).ToArray();
        var workload = parameterOrder.Where(name => IsConstant(name) && !identityOnly.Contains(name))
            .Select(name => new BenchmarkParameterView { Name = name, Value = parameterValues[name].First() })
            .ToArray();

        var contenderNames = new List<string>();
        var contenderLibrary = new Dictionary<string, string>(StringComparer.Ordinal);
        var caseOrder = new List<string>();
        var cases = new Dictionary<string, (string Label, BenchmarkParameterView[] Parameters, Dictionary<string, NormalizedRow> Cells)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var values = varying
                .Select(name => (Name: name, Found: row.TryGetValue(name, out var value), Value: value))
                .ToArray();
            var parameters = values
                .Select(item => new BenchmarkParameterView { Name = item.Name, Value = item.Found ? item.Value : "—" })
                .ToArray();
            // The key is structural, so values that contain the label separator cannot merge two cases.
            var key = string.Join("\u001f", values.Select(item => item.Found ? "1" + item.Value : "0"));
            if (!cases.ContainsKey(key))
            {
                var label = string.Join(" · ", parameters.Select(p => varying.Length > 1 ? p.Name + " " + p.Value : p.Value));
                cases[key] = (label, parameters, new Dictionary<string, NormalizedRow>(StringComparer.Ordinal));
                caseOrder.Add(key);
            }
            cases[key].Cells[row.Contender] = row;
            if (!contenderNames.Contains(row.Contender, StringComparer.Ordinal))
            {
                contenderNames.Add(row.Contender);
                contenderLibrary[row.Contender] = row.Library;
            }
        }
        bool IsSubject(string contender) => string.Equals(contenderLibrary[contender], subject, StringComparison.OrdinalIgnoreCase);

        var fastestByCase = caseOrder.ToDictionary(
            label => label,
            label => cases[label].Cells.Values.Where(Completed).Select(row => row.TimeMs!.Value).DefaultIfEmpty(double.NaN).Min(),
            StringComparer.Ordinal);

        var stats = contenderNames.ToDictionary(name => name, name =>
        {
            var ratios = new List<double>();
            var wins = 0;
            foreach (var label in caseOrder)
            {
                if (!cases[label].Cells.TryGetValue(name, out var row) || !Completed(row) || double.IsNaN(fastestByCase[label]))
                    continue;
                var ratio = row.TimeMs!.Value / fastestByCase[label];
                ratios.Add(ratio);
                if (ratio < 1.005) wins++;
            }
            return (Ratio: GeometricMean(ratios), Wins: wins, Cases: ratios.Count);
        }, StringComparer.Ordinal);

        var ordered = contenderNames
            .OrderBy(name => IsSubject(name) ? 0 : 1)
            .ThenBy(name => stats[name].Ratio ?? double.MaxValue)
            .ToArray();
        var ranking = contenderNames
            .Where(name => stats[name].Ratio.HasValue)
            .OrderBy(name => stats[name].Ratio!.Value)
            .ToList();

        var versions = ReadVersions(result.Metadata);
        var contenders = ordered.Select(name =>
        {
            var library = rows.First(row => row.Contender == name).Library;
            return new BenchmarkContenderView
            {
                Name = name,
                Library = library,
                IsSubject = IsSubject(name),
                Version = versions.TryGetValue(Key(library), out var version) ? version : string.Empty,
                Relative = Ratio(stats[name].Ratio),
                Wins = stats[name].Wins,
                Cases = stats[name].Cases,
                Rank = ranking.IndexOf(name) + 1
            };
        }).ToArray();

        var caseViews = caseOrder.Select(label =>
        {
            var (caseLabel, parameters, cells) = cases[label];
            var fastest = fastestByCase[label];
            var slowest = cells.Values.Where(Completed).Select(row => row.TimeMs!.Value).DefaultIfEmpty(0).Max();
            return new BenchmarkCaseView
            {
                Label = caseLabel,
                Parameters = parameters,
                Cells = ordered.Select(name =>
                {
                    if (!cells.TryGetValue(name, out var row))
                        return new BenchmarkCellView { Contender = name, IsSubject = IsSubject(name), Missing = true };
                    var completed = Completed(row);
                    return new BenchmarkCellView
                    {
                        Contender = name,
                        IsSubject = IsSubject(name),
                        Failed = row.Failed,
                        TimeMs = row.TimeMs,
                        TimeText = row.Failed ? "Failed" : FormatTime(row.TimeMs),
                        AllocatedBytes = row.AllocatedBytes,
                        AllocatedText = FormatBytes(row.AllocatedBytes),
                        Relative = completed && !double.IsNaN(fastest) ? Ratio(row.TimeMs!.Value / fastest) : new BenchmarkRatioView(),
                        BarPercent = completed && slowest > 0 ? Math.Round(Math.Max(1.5, row.TimeMs!.Value / slowest * 100), 1) : 0
                    };
                }).ToArray()
            };
        }).ToArray();

        var subjects = ordered.Where(name => IsSubject(name)).ToArray();
        var others = ordered.Where(name => !IsSubject(name)).ToArray();
        var headToHead = subjects
            .SelectMany(a => others.Select(b => HeadToHead(a, b, caseOrder, cases)))
            .Where(view => view.Cases > 0)
            .ToArray();

        var platform = (entry.Platform ?? string.Empty).ToLowerInvariant();
        var runMode = (entry.RunMode ?? string.Empty).ToLowerInvariant();
        return new BenchmarkRunView
        {
            Id = platform + "-" + runMode,
            Platform = platform,
            PlatformLabel = PlatformLabel(platform),
            RunMode = runMode,
            IsFull = runMode == "full",
            Published = entry.Publish,
            GeneratedUtc = entry.GeneratedUtc,
            Date = entry.GeneratedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ResultPath = entry.ResultPath,
            SourceCommit = Metadata(result.Metadata, "gitSha") ?? Metadata(result.Metadata, "benchmark.workload.sourceCommit") ?? string.Empty,
            Environment = BuildEnvironment(result, entry),
            Versions = versions,
            Statistic = statistic,
            Workload = workload,
            Contenders = contenders,
            Cases = caseViews,
            HeadToHead = headToHead
        };
    }

    private static BenchmarkHeadToHeadView HeadToHead(
        string a,
        string b,
        IEnumerable<string> caseOrder,
        IReadOnlyDictionary<string, (string Label, BenchmarkParameterView[] Parameters, Dictionary<string, NormalizedRow> Cells)> cases)
    {
        var time = new List<double>();
        var memory = new List<double>();
        foreach (var label in caseOrder)
        {
            var cells = cases[label].Cells;
            if (!cells.TryGetValue(a, out var x) || !cells.TryGetValue(b, out var y) || !Completed(x) || !Completed(y) || y.TimeMs <= 0)
                continue;
            time.Add(x.TimeMs!.Value / y.TimeMs!.Value);
            if (x.AllocatedBytes > 0 && y.AllocatedBytes > 0)
                memory.Add(x.AllocatedBytes!.Value / y.AllocatedBytes!.Value);
        }

        var timeRatio = GeometricMean(time);
        var memoryRatio = GeometricMean(memory);
        var outcome = timeRatio is null ? "even" : timeRatio < 1 - EvenTolerance ? "ahead" : timeRatio > 1 + EvenTolerance ? "behind" : "even";
        var text = timeRatio is null ? string.Empty
            : outcome == "ahead" ? $"{a} is {FormatRatio(1 / timeRatio.Value)}× faster than {b}"
            : outcome == "behind" ? $"{b} is {FormatRatio(timeRatio.Value)}× faster than {a}"
            : $"{a} and {b} are about as fast";

        var detail = new List<string>();
        if (memoryRatio is double m)
        {
            detail.Add(m < 1 - EvenTolerance ? $"{a} allocates {FormatRatio(1 / m)}× less"
                : m > 1 + EvenTolerance ? $"{a} allocates {FormatRatio(m)}× more"
                : "similar allocations");
        }
        detail.Add(time.Count == 1 ? "1 case" : $"geometric mean of {time.Count} cases");

        return new BenchmarkHeadToHeadView
        {
            Subject = a,
            Other = b,
            TimeRatio = timeRatio,
            AllocationRatio = memoryRatio,
            Cases = time.Count,
            Outcome = outcome,
            Text = text,
            Detail = string.Join(" · ", detail)
        };
    }

    private sealed class NormalizedRow
    {
        public string Contender = string.Empty;
        public string Library = string.Empty;
        public Dictionary<string, string> Parameters = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Identity = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGetValue(string name, out string value)
            => Parameters.TryGetValue(name, out value!) || Identity.TryGetValue(name, out value!);
        public double? TimeMs;
        public string Statistic = "median";
        public double? AllocatedBytes;
        public bool Failed;
    }

    private static bool Completed(NormalizedRow row) => !row.Failed && row.TimeMs is > 0;

    private static List<NormalizedRow> NormalizeRows(IEnumerable<BenchmarkSummaryRow> summary, string[] libraries, string subject)
    {
        var declared = libraries
            .Select(name => (Name: name, Key: Key(name)))
            .Where(library => library.Key.Length > 0)
            .OrderByDescending(library => library.Key.Length)
            .ToArray();

        var parsed = summary.Select(row =>
        {
            var raw = string.IsNullOrWhiteSpace(row.Scenario) ? row.Engine ?? string.Empty : row.Scenario;
            var (library, suffix) = MatchLibrary(raw, declared);
            return (Row: row, Library: library, Suffix: suffix);
        }).ToArray();

        // A suffix that follows two or more libraries names an operation they all ran.
        var operations = new HashSet<string>(
            parsed.Where(item => item.Suffix.Length > 0)
                .GroupBy(item => item.Suffix, StringComparer.Ordinal)
                .Where(group => group.Select(item => item.Library).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);

        return parsed.Select(item =>
        {
            var (parameters, identity) = CaseParameters(item.Row);
            var contender = item.Library;
            if (item.Suffix.Length > 0)
            {
                // "EncodeParallel" is the Encode operation run by a Parallel variant of the library.
                var operation = operations
                    .Where(op => item.Suffix.StartsWith(op, StringComparison.Ordinal))
                    .OrderByDescending(op => op.Length)
                    .FirstOrDefault();
                // A recorded operation is authoritative; a suffix only names one when none was recorded.
                if (operation is not null && !identity.ContainsKey("Operation")) parameters["Operation"] = operation;
                var variant = (operation is null ? item.Suffix : item.Suffix.Substring(operation.Length)).Trim();
                if (variant.Length > 0) contender = item.Library + " " + variant;
            }

            var time = item.Row.MedianMs ?? item.Row.MeanMs;
            return new NormalizedRow
            {
                Contender = contender,
                Library = item.Library,
                Parameters = parameters,
                Identity = identity,
                TimeMs = time,
                Statistic = item.Row.MedianMs.HasValue ? "median" : "mean",
                AllocatedBytes = Metric(item.Row.Metrics, "BytesAllocatedPerOperation") ?? Metric(item.Row.Metrics, "Allocated"),
                Failed = !string.IsNullOrWhiteSpace(item.Row.Status) && !string.Equals(item.Row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)
            };
        }).ToList();
    }

    private static (string Library, string Suffix) MatchLibrary(string raw, (string Name, string Key)[] declared)
    {
        var flat = Key(raw);
        foreach (var library in declared)
        {
            if (!flat.StartsWith(library.Key, StringComparison.Ordinal))
                continue;

            // Walk the raw name until the library's letters and digits are consumed; the rest is the suffix.
            var consumed = 0;
            var index = 0;
            while (index < raw.Length && consumed < library.Key.Length)
            {
                if (char.IsLetterOrDigit(raw[index])) consumed++;
                index++;
            }
            var rest = raw.Substring(index).TrimStart(' ', '_', '-', '.').Replace('_', ' ');
            return (library.Name, rest);
        }

        return (raw.Replace('_', ' '), string.Empty);
    }

    /// <summary>
    /// Splits a row into case parameters and identity values. Identity values (the recorded operation, the
    /// BenchmarkDotNet job and its characteristics) keep different configurations apart but are only shown
    /// when they differ between rows.
    /// </summary>
    private static (Dictionary<string, string> Parameters, Dictionary<string, string> Identity) CaseParameters(BenchmarkSummaryRow row)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var variables = (IReadOnlyDictionary<string, string?>?)row.Variables ?? new Dictionary<string, string?>();

        // "Run" is the importer's placeholder for a BenchmarkDotNet method, not a recorded operation.
        if (!string.IsNullOrWhiteSpace(row.Operation) && !string.Equals(row.Operation, "Run", StringComparison.Ordinal))
            identity["Operation"] = row.Operation;

        var fromBenchmarkDotNet = variables.ContainsKey("FullName") || variables.ContainsKey("BenchmarkDotNetReport");
        if (fromBenchmarkDotNet && !string.IsNullOrWhiteSpace(row.Engine))
            identity["Job"] = row.Engine;

        // BenchmarkDotNet's full name lists every parameter exactly; results imported before the importer
        // recovered them from it can hold several parameters joined in one value.
        var fromFullName = variables.TryGetValue("FullName", out var fullName) &&
            BenchmarkDotNetColumns.TryParseFullNameParameters(fullName, out var listed)
                ? listed
                : null;
        if (fromFullName is not null)
        {
            foreach (var parameter in fromFullName)
                parameters[parameter.Key] = parameter.Value;
        }

        // Rows imported from a BenchmarkDotNet CSV report before trailing statistics were separated still carry them.
        var fromReport = variables.ContainsKey("BenchmarkDotNetReport");
        foreach (var variable in variables)
        {
            if (BenchmarkDotNetColumns.IsJobCharacteristic(variable.Key))
            {
                if (!string.Equals(variable.Key, "FullName", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(variable.Key, "Namespace", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(variable.Key, "Type", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(variable.Key, "BenchmarkDotNetReport", StringComparison.OrdinalIgnoreCase))
                    identity[variable.Key] = variable.Value ?? string.Empty;
                continue;
            }
            if (fromFullName is not null || (fromReport && BenchmarkDotNetColumns.IsStatistic(variable.Key)))
                continue;
            parameters[variable.Key] = variable.Value ?? string.Empty;
        }
        return (parameters, identity);
    }

    private static Dictionary<string, string> ReadVersions(IReadOnlyDictionary<string, string>? metadata)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null) return versions;
        const string prefix = "benchmark.dependency.";
        const string suffix = ".version";
        foreach (var item in metadata)
        {
            if (item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Value))
            {
                var id = item.Key.Substring(prefix.Length, item.Key.Length - prefix.Length - suffix.Length);
                if (id.Length > 0 && id.IndexOf('.') < 0)
                    versions[Key(id)] = item.Value;
            }
        }
        return versions;
    }

    private static BenchmarkRunEnvironmentView BuildEnvironment(BenchmarkRunResult result, BenchmarkEvidenceEntry entry)
    {
        var source = HasValue(result.Environment) ? result.Environment : entry.Environment ?? new BenchmarkEnvironmentInfo();
        var view = new BenchmarkRunEnvironmentView
        {
            Processor = source.ProcessorName ?? string.Empty,
            OperatingSystem = TrimAfter(source.OsDescription, " ["),
            Runtime = TrimAfter(source.RuntimeVersion, " ("),
            LogicalCores = source.LogicalCoreCount,
            Affinity = Metadata(result.Metadata, "benchmark.workload.affinityMask") ?? string.Empty
        };
        view.Recorded = view.Processor.Length > 0 || view.OperatingSystem.Length > 0 || view.Runtime.Length > 0;

        var parts = new List<string>();
        if (view.Processor.Length > 0)
            parts.Add(view.LogicalCores is int cores ? $"{view.Processor} ({cores.ToString(CultureInfo.InvariantCulture)} threads)" : view.Processor);
        if (view.OperatingSystem.Length > 0) parts.Add(view.OperatingSystem);
        if (view.Runtime.Length > 0) parts.Add(view.Runtime);
        if (view.Affinity.Length > 0) parts.Add("CPU affinity " + view.Affinity);
        view.Text = view.Recorded ? string.Join(" · ", parts) : "Environment not recorded";
        return view;
    }

    private static bool HasValue(BenchmarkEnvironmentInfo? environment)
        => environment is not null &&
           (!string.IsNullOrWhiteSpace(environment.ProcessorName) ||
            !string.IsNullOrWhiteSpace(environment.OsDescription) ||
            !string.IsNullOrWhiteSpace(environment.RuntimeVersion));

    private static string TrimAfter(string? text, string marker)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var index = text!.IndexOf(marker, StringComparison.Ordinal);
        return (index < 0 ? text : text.Substring(0, index)).Trim();
    }

    private static string? Metadata(IReadOnlyDictionary<string, string>? metadata, string key)
        => metadata is not null && metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static double? Metric(IReadOnlyDictionary<string, double>? metrics, string key)
        => metrics is not null && metrics.TryGetValue(key, out var value) && !double.IsNaN(value) && !double.IsInfinity(value) ? value : null;

    private static string PlatformLabel(string platform) => platform switch
    {
        "windows" => "Windows",
        "linux" => "Linux",
        "macos" => "macOS",
        _ => platform
    };

    private static string Key(string? text)
        => new string((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static double? GeometricMean(IReadOnlyCollection<double> values)
    {
        var positive = values.Where(value => value > 0 && !double.IsInfinity(value)).ToArray();
        return positive.Length == 0 ? null : Math.Exp(positive.Sum(Math.Log) / positive.Length);
    }

    private static BenchmarkRatioView Ratio(double? ratio)
        => ratio is double value
            ? new BenchmarkRatioView { Ratio = value, IsFastest = value < 1.005, Text = value < 1.005 ? "1.00×" : FormatRatio(value) + "×" }
            : new BenchmarkRatioView();

    /// <summary>Formats a ratio with two decimals below 2, one below 10, none above.</summary>
    public static string FormatRatio(double ratio)
        => ratio.ToString(ratio >= 10 ? "0" : ratio >= 1.995 ? "0.0" : "0.00", CultureInfo.InvariantCulture);

    /// <summary>Formats milliseconds as s, ms or µs with three significant figures.</summary>
    public static string FormatTime(double? milliseconds)
    {
        if (milliseconds is not double ms || double.IsNaN(ms)) return "—";
        if (ms >= 1000) return (ms / 1000).ToString(ms >= 10000 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " s";
        if (ms >= 100) return ms.ToString("0", CultureInfo.InvariantCulture) + " ms";
        if (ms >= 10) return ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        if (ms >= 1) return ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
        return (ms * 1000).ToString(ms >= 0.1 ? "0" : "0.0", CultureInfo.InvariantCulture) + " µs";
    }

    /// <summary>Formats bytes as GB, MB, KB or B.</summary>
    public static string FormatBytes(double? bytes)
    {
        if (bytes is not double b || double.IsNaN(b)) return string.Empty;
        if (b >= 1073741824) return (b / 1073741824).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        if (b >= 1048576) return (b / 1048576).ToString(b >= 104857600 ? "0" : "0.0", CultureInfo.InvariantCulture) + " MB";
        if (b >= 1024) return (b / 1024).ToString("0", CultureInfo.InvariantCulture) + " KB";
        return b.ToString("0", CultureInfo.InvariantCulture) + " B";
    }
}
