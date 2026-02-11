namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string? WriteCoverageReport(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        string? assemblyName,
        string? assemblyVersion,
        List<string> warnings)
    {
        if (options is null || !options.GenerateCoverageReport)
            return null;

        var configuredPath = string.IsNullOrWhiteSpace(options.CoverageReportPath)
            ? "coverage.json"
            : options.CoverageReportPath!.Trim();
        var reportPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.Combine(outputPath, configuredPath);

        try
        {
            var payload = BuildCoveragePayload(types, assemblyName, assemblyVersion);
            var parent = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            WriteJson(reportPath, payload);
            return reportPath;
        }
        catch (Exception ex)
        {
            warnings?.Add($"API docs coverage: failed to write report '{reportPath}' ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static Dictionary<string, object?> BuildCoveragePayload(
        IReadOnlyList<ApiTypeModel> types,
        string? assemblyName,
        string? assemblyVersion)
    {
        var safeTypes = types ?? Array.Empty<ApiTypeModel>();
        var typeCount = safeTypes.Count;
        var typesWithSummary = safeTypes.Count(static t => !string.IsNullOrWhiteSpace(t.Summary));
        var typesWithRemarks = safeTypes.Count(static t => !string.IsNullOrWhiteSpace(t.Remarks));
        var typesWithCodeExamples = safeTypes.Count(static t => t.Examples.Any(static ex =>
            ex.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ex.Text)));

        var totalMembers = safeTypes.Sum(static t => t.Methods.Count + t.Constructors.Count + t.Properties.Count + t.Fields.Count + t.Events.Count + t.ExtensionMethods.Count);
        var allMembers = safeTypes
            .SelectMany(static t => t.Methods
                .Concat(t.Constructors)
                .Concat(t.Properties)
                .Concat(t.Fields)
                .Concat(t.Events)
                .Concat(t.ExtensionMethods))
            .ToArray();
        var membersWithSummary = allMembers.Count(static m => !string.IsNullOrWhiteSpace(m.Summary));
        var membersWithCodeExamples = allMembers.Count(static m => m.Examples.Any(static ex =>
            ex.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ex.Text)));
        var typeSourceCoverage = AnalyzeSourceCoverage(safeTypes.Select(static t => t.Source));
        var memberSourceCoverage = AnalyzeSourceCoverage(allMembers.Select(static m => m.Source));

        var kinds = safeTypes
            .GroupBy(static t => string.IsNullOrWhiteSpace(t.Kind) ? "Unknown" : t.Kind, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var commandTypes = safeTypes.Where(IsPowerShellCommandType).ToArray();
        var commandCount = commandTypes.Length;
        var commandsWithSummary = commandTypes.Count(static c => !string.IsNullOrWhiteSpace(c.Summary));
        var commandsWithRemarks = commandTypes.Count(static c => !string.IsNullOrWhiteSpace(c.Remarks));
        var commandsWithCodeExamples = commandTypes.Count(static c => c.Examples.Any(static ex =>
            ex.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ex.Text)));
        var commandSourceCoverage = AnalyzeSourceCoverage(commandTypes.Select(static c => c.Source));

        var commandsMissingExamples = commandTypes
            .Where(static c => !c.Examples.Any(static ex =>
                ex.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(ex.Text)))
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();

        var commandParameterCoverage = commandTypes.Select(static c => new
            {
                Type = c,
                Parameters = c.Methods.SelectMany(static m => m.Parameters).ToArray()
            })
            .ToArray();
        var commandParameterCount = commandParameterCoverage.Sum(static x => x.Parameters.Length);
        var commandParametersWithSummary = commandParameterCoverage.Sum(static x => x.Parameters.Count(static p => !string.IsNullOrWhiteSpace(p.Summary)));

        static Dictionary<string, object?> MakeCoverage(int total, int covered)
        {
            var percent = total <= 0 ? 100d : Math.Round((covered * 100d) / total, 2, MidpointRounding.AwayFromZero);
            return new Dictionary<string, object?>
            {
                ["covered"] = covered,
                ["total"] = total,
                ["percent"] = percent
            };
        }

        return new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["assembly"] = new Dictionary<string, object?>
            {
                ["assemblyName"] = assemblyName ?? string.Empty,
                ["assemblyVersion"] = assemblyVersion
            },
            ["types"] = new Dictionary<string, object?>
            {
                ["count"] = typeCount,
                ["byKind"] = kinds,
                ["summary"] = MakeCoverage(typeCount, typesWithSummary),
                ["remarks"] = MakeCoverage(typeCount, typesWithRemarks),
                ["codeExamples"] = MakeCoverage(typeCount, typesWithCodeExamples)
            },
            ["members"] = new Dictionary<string, object?>
            {
                ["count"] = totalMembers,
                ["summary"] = MakeCoverage(totalMembers, membersWithSummary),
                ["codeExamples"] = MakeCoverage(totalMembers, membersWithCodeExamples)
            },
            ["source"] = new Dictionary<string, object?>
            {
                ["types"] = BuildSourceCoveragePayload(typeCount, typeSourceCoverage),
                ["members"] = BuildSourceCoveragePayload(totalMembers, memberSourceCoverage),
                ["powershell"] = BuildSourceCoveragePayload(commandCount, commandSourceCoverage)
            },
            ["powershell"] = new Dictionary<string, object?>
            {
                ["commandCount"] = commandCount,
                ["summary"] = MakeCoverage(commandCount, commandsWithSummary),
                ["remarks"] = MakeCoverage(commandCount, commandsWithRemarks),
                ["codeExamples"] = MakeCoverage(commandCount, commandsWithCodeExamples),
                ["parameters"] = MakeCoverage(commandParameterCount, commandParametersWithSummary),
                ["commandsMissingCodeExamples"] = commandsMissingExamples
            }
        };
    }

    private static Dictionary<string, object?> BuildSourceCoveragePayload(int total, SourceCoverageStats coverage)
    {
        return new Dictionary<string, object?>
        {
            ["count"] = total,
            ["path"] = BuildCoveragePercent(total, coverage.WithPathCount),
            ["urlPresent"] = BuildCoveragePercent(total, coverage.WithUrlCount),
            ["url"] = BuildCoveragePercent(total, coverage.ValidUrlCount),
            ["invalidUrl"] = new Dictionary<string, object?>
            {
                ["count"] = coverage.InvalidUrlCount,
                ["samples"] = coverage.InvalidUrlSamples
            },
            ["unresolvedTemplateToken"] = new Dictionary<string, object?>
            {
                ["count"] = coverage.UnresolvedTemplateTokenCount,
                ["samples"] = coverage.UnresolvedTemplateSamples
            },
            ["repoMismatchHints"] = new Dictionary<string, object?>
            {
                ["count"] = coverage.RepoMismatchHintCount,
                ["samples"] = coverage.RepoMismatchSamples
            }
        };
    }

    private static Dictionary<string, object?> BuildCoveragePercent(int total, int covered)
    {
        var percent = total <= 0 ? 100d : Math.Round((covered * 100d) / total, 2, MidpointRounding.AwayFromZero);
        return new Dictionary<string, object?>
        {
            ["covered"] = covered,
            ["total"] = total,
            ["percent"] = percent
        };
    }

    private static SourceCoverageStats AnalyzeSourceCoverage(IEnumerable<ApiSourceLink?> sources)
    {
        const int sampleLimit = 12;
        var stats = new SourceCoverageStats();
        if (sources is null)
            return stats;

        var invalidSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mismatchSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (!string.IsNullOrWhiteSpace(source?.Path))
                stats.WithPathCount++;

            var url = source?.Url;
            if (string.IsNullOrWhiteSpace(url))
                continue;

            stats.WithUrlCount++;
            if (HasUnresolvedTemplateToken(url))
            {
                stats.UnresolvedTemplateTokenCount++;
                if (unresolvedSamples.Count < sampleLimit)
                    unresolvedSamples.Add(url.Trim());
            }

            if (TryParseWebUrl(url, out var uri))
            {
                stats.ValidUrlCount++;
                if (IsGitHubRepoMismatchHint(source?.Path, uri, out var mismatchHint))
                {
                    stats.RepoMismatchHintCount++;
                    if (mismatchSamples.Count < sampleLimit && !string.IsNullOrWhiteSpace(mismatchHint))
                        mismatchSamples.Add(mismatchHint);
                }
            }
            else
            {
                stats.InvalidUrlCount++;
                if (invalidSamples.Count < sampleLimit)
                    invalidSamples.Add(url.Trim());
            }
        }

        stats.InvalidUrlSamples = invalidSamples.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        stats.UnresolvedTemplateSamples = unresolvedSamples.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        stats.RepoMismatchSamples = mismatchSamples.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return stats;
    }

    private static bool TryParseWebUrl(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
            return false;
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnresolvedTemplateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.IndexOf('{', StringComparison.Ordinal) >= 0 ||
               value.IndexOf('}', StringComparison.Ordinal) >= 0 ||
               value.IndexOf("%7B", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("%7D", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsGitHubRepoMismatchHint(string? sourcePath, Uri url, out string hint)
    {
        hint = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;
        if (!string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = sourcePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], segments[1], StringComparison.OrdinalIgnoreCase))
            return false;

        var uriSegments = url.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (uriSegments.Length < 2)
            return false;

        var inferredRepo = segments[0];
        var urlRepo = uriSegments[1];
        if (string.Equals(inferredRepo, urlRepo, StringComparison.OrdinalIgnoreCase))
            return false;

        hint = $"{inferredRepo} -> {urlRepo}";
        return true;
    }

    private sealed class SourceCoverageStats
    {
        public int WithPathCount { get; set; }
        public int WithUrlCount { get; set; }
        public int ValidUrlCount { get; set; }
        public int InvalidUrlCount { get; set; }
        public int UnresolvedTemplateTokenCount { get; set; }
        public int RepoMismatchHintCount { get; set; }
        public string[] InvalidUrlSamples { get; set; } = Array.Empty<string>();
        public string[] UnresolvedTemplateSamples { get; set; } = Array.Empty<string>();
        public string[] RepoMismatchSamples { get; set; } = Array.Empty<string>();
    }
}
