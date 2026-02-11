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
}
