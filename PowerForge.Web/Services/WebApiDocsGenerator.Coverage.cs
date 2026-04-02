namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string? WriteCoverageReport(
        string outputPath,
        WebApiDocsOptions options,
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
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
            var payload = BuildCoveragePayload(types, options, typeRelatedContentMap, assemblyName, assemblyVersion);
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

    private static void AppendSourceCoverageWarnings(
        IReadOnlyList<ApiTypeModel> types,
        List<string> warnings)
    {
        if (types is null || warnings is null)
            return;

        var allMembers = types
            .SelectMany(static t => t.Methods
                .Concat(t.Constructors)
                .Concat(t.Properties)
                .Concat(t.Fields)
                .Concat(t.Events)
                .Concat(t.ExtensionMethods))
            .ToArray();
        var commandTypes = types.Where(IsPowerShellCommandType).ToArray();

        var groups = new[]
        {
            new SourceCoverageGroup("types", AnalyzeSourceCoverage(types.Select(static t => t.Source))),
            new SourceCoverageGroup("members", AnalyzeSourceCoverage(allMembers.Select(static m => m.Source))),
            new SourceCoverageGroup("powershell", AnalyzeSourceCoverage(commandTypes.Select(static c => c.Source)))
        };

        AppendSourceCoverageWarning(
            groups,
            static group => group.Stats.UnresolvedTemplateTokenCount,
            static group => group.Stats.UnresolvedTemplateSamples,
            "generated source URLs still contain unresolved template tokens",
            warnings);
        AppendSourceCoverageWarning(
            groups,
            static group => group.Stats.InvalidUrlCount,
            static group => group.Stats.InvalidUrlSamples,
            "generated source URLs are not valid http/https URLs",
            warnings);
        AppendSourceCoverageWarning(
            groups,
            static group => group.Stats.RepoMismatchHintCount,
            static group => group.Stats.RepoMismatchUrlSamples,
            "generated GitHub source URLs look mismatched to the inferred repo and may 404",
            warnings);
    }

    private static void AppendPowerShellExampleQualityWarnings(
        IReadOnlyList<ApiTypeModel> types,
        List<string> warnings)
    {
        if (types is null || warnings is null)
            return;

        var commands = types.Where(IsPowerShellCommandType).ToArray();
        if (commands.Length == 0)
            return;

        var generatedOnly = commands
            .Where(HasOnlyGeneratedPowerShellFallbackExamples)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (generatedOnly.Length > 0)
        {
            var preview = string.Join(", ", generatedOnly.Take(4));
            var more = generatedOnly.Length > 4 ? $" (+{generatedOnly.Length - 4} more)" : string.Empty;
            warnings.Add(
                $"API docs PowerShell coverage: {generatedOnly.Length} command(s) rely only on generated fallback examples. " +
                $"Add authored examples or example scripts for better docs quality (samples: {preview}{more}).");
        }

        var playbackWithoutPoster = commands
            .Where(HasImportedPlaybackMediaWithoutPoster)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (playbackWithoutPoster.Length == 0)
            return;

        var posterPreview = string.Join(", ", playbackWithoutPoster.Take(4));
        var posterMore = playbackWithoutPoster.Length > 4 ? $" (+{playbackWithoutPoster.Length - 4} more)" : string.Empty;
        warnings.Add(
            $"API docs PowerShell coverage: {playbackWithoutPoster.Length} command(s) expose imported playback media without poster art. " +
            $"Add matching .png/.jpg/.jpeg/.webp sidecars for better docs quality (samples: {posterPreview}{posterMore}).");
    }

    private static void AppendRelatedContentCoverageWarnings(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
        List<string> warnings)
    {
        if (types is null || options is null || warnings is null)
            return;
        if (options.QuickStartTypeNames.Count == 0)
            return;

        var configuredQuickStartTypes = GetConfiguredQuickStartTypes(types, options);
        if (configuredQuickStartTypes.Count == 0)
            return;

        var missing = configuredQuickStartTypes
            .Where(type => !TypeHasRelatedContent(type, typeRelatedContentMap))
            .Select(static type => type.FullName)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length == 0)
            return;

        var preview = string.Join(", ", missing.Take(6));
        var suffix = missing.Length > 6 ? $" (+{missing.Length - 6} more)" : string.Empty;
        warnings.Add(
            $"API docs related content: {missing.Length} configured quickStart type(s) do not have curated guides or samples attached. " +
            $"Add related-content manifests for better entry-point guidance (samples: {preview}{suffix}).");
    }

    private static Dictionary<string, object?> BuildCoveragePayload(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap,
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
        var typesWithRelatedContent = safeTypes.Count(type => TypeHasRelatedContent(type, typeRelatedContentMap));

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
        var membersWithRelatedContent = allMembers.Count(member => MemberHasRelatedContent(member, typeRelatedContentMap));
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
        var commandsWithCodeExamples = commandTypes.Count(HasCodeExamples);
        var commandsWithAuthoredHelpCodeExamples = commandTypes.Count(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.AuthoredHelp));
        var commandsWithImportedScriptCodeExamples = commandTypes.Count(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.ImportedScript));
        var commandsWithGeneratedFallbackCodeExamples = commandTypes.Count(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.GeneratedFallback));
        var commandsWithGeneratedFallbackOnlyExamples = commandTypes.Count(HasOnlyGeneratedPowerShellFallbackExamples);
        var commandsWithImportedScriptPlaybackMedia = commandTypes.Count(HasImportedPlaybackMedia);
        var commandsWithImportedScriptPlaybackMediaWithPoster = commandTypes.Count(HasImportedPlaybackMediaWithPoster);
        var commandsWithImportedScriptPlaybackMediaWithoutPoster = commandTypes.Count(HasImportedPlaybackMediaWithoutPoster);
        var commandsWithImportedScriptPlaybackMediaUnsupportedSidecars = commandTypes.Count(HasImportedPlaybackMediaWithUnsupportedSidecars);
        var commandsWithImportedScriptPlaybackMediaOversizedAssets = commandTypes.Count(HasImportedPlaybackMediaWithOversizedAssets);
        var commandsWithImportedScriptPlaybackMediaStaleAssets = commandTypes.Count(HasImportedPlaybackMediaWithStaleAssets);
        var commandSourceCoverage = AnalyzeSourceCoverage(commandTypes.Select(static c => c.Source));

        var commandsMissingExamples = commandTypes
            .Where(static c => !HasCodeExamples(c))
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingGeneratedFallbackOnlyExamples = commandTypes
            .Where(HasOnlyGeneratedPowerShellFallbackExamples)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingAuthoredHelpCodeExamples = commandTypes
            .Where(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.AuthoredHelp))
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptCodeExamples = commandTypes
            .Where(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.ImportedScript))
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingGeneratedFallbackCodeExamples = commandTypes
            .Where(static c => HasCodeExamplesFromOrigin(c, ApiExampleOrigins.GeneratedFallback))
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptPlaybackMedia = commandTypes
            .Where(HasImportedPlaybackMedia)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptPlaybackMediaWithoutPoster = commandTypes
            .Where(HasImportedPlaybackMediaWithoutPoster)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars = commandTypes
            .Where(HasImportedPlaybackMediaWithUnsupportedSidecars)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptPlaybackMediaOversizedAssets = commandTypes
            .Where(HasImportedPlaybackMediaWithOversizedAssets)
            .Select(static c => c.FullName)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        var commandsUsingImportedScriptPlaybackMediaStaleAssets = commandTypes
            .Where(HasImportedPlaybackMediaWithStaleAssets)
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
        var configuredQuickStartTypes = GetConfiguredQuickStartTypes(safeTypes, options);
        var quickStartTypesWithRelatedContent = configuredQuickStartTypes.Count(type => TypeHasRelatedContent(type, typeRelatedContentMap));
        var quickStartTypesMissingRelatedContent = configuredQuickStartTypes
            .Where(type => !TypeHasRelatedContent(type, typeRelatedContentMap))
            .Select(static type => type.FullName)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();

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
                ["codeExamples"] = MakeCoverage(typeCount, typesWithCodeExamples),
                ["relatedContent"] = MakeCoverage(typeCount, typesWithRelatedContent),
                ["quickStartRelatedContent"] = MakeCoverage(configuredQuickStartTypes.Count, quickStartTypesWithRelatedContent),
                ["quickStartMissingRelatedContent"] = new Dictionary<string, object?>
                {
                    ["count"] = quickStartTypesMissingRelatedContent.Length,
                    ["types"] = quickStartTypesMissingRelatedContent
                }
            },
            ["members"] = new Dictionary<string, object?>
            {
                ["count"] = totalMembers,
                ["summary"] = MakeCoverage(totalMembers, membersWithSummary),
                ["codeExamples"] = MakeCoverage(totalMembers, membersWithCodeExamples),
                ["relatedContent"] = MakeCoverage(totalMembers, membersWithRelatedContent)
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
                ["authoredHelpCodeExamples"] = MakeCoverage(commandCount, commandsWithAuthoredHelpCodeExamples),
                ["importedScriptCodeExamples"] = MakeCoverage(commandCount, commandsWithImportedScriptCodeExamples),
                ["generatedFallbackCodeExamples"] = MakeCoverage(commandCount, commandsWithGeneratedFallbackCodeExamples),
                ["generatedFallbackOnlyExamples"] = MakeCoverage(commandCount, commandsWithGeneratedFallbackOnlyExamples),
                ["importedScriptPlaybackMedia"] = MakeCoverage(commandCount, commandsWithImportedScriptPlaybackMedia),
                ["importedScriptPlaybackMediaWithPoster"] = MakeCoverage(commandsWithImportedScriptPlaybackMedia, commandsWithImportedScriptPlaybackMediaWithPoster),
                ["importedScriptPlaybackMediaWithoutPoster"] = MakeCoverage(commandsWithImportedScriptPlaybackMedia, commandsWithImportedScriptPlaybackMediaWithoutPoster),
                ["importedScriptPlaybackMediaUnsupportedSidecars"] = MakeCoverage(commandsWithImportedScriptPlaybackMedia, commandsWithImportedScriptPlaybackMediaUnsupportedSidecars),
                ["importedScriptPlaybackMediaOversizedAssets"] = MakeCoverage(commandsWithImportedScriptPlaybackMedia, commandsWithImportedScriptPlaybackMediaOversizedAssets),
                ["importedScriptPlaybackMediaStaleAssets"] = MakeCoverage(commandsWithImportedScriptPlaybackMedia, commandsWithImportedScriptPlaybackMediaStaleAssets),
                ["parameters"] = MakeCoverage(commandParameterCount, commandParametersWithSummary),
                ["commandsMissingCodeExamples"] = commandsMissingExamples,
                ["commandsUsingAuthoredHelpCodeExamples"] = commandsUsingAuthoredHelpCodeExamples,
                ["commandsUsingImportedScriptCodeExamples"] = commandsUsingImportedScriptCodeExamples,
                ["commandsUsingGeneratedFallbackCodeExamples"] = commandsUsingGeneratedFallbackCodeExamples,
                ["commandsUsingGeneratedFallbackOnlyExamples"] = commandsUsingGeneratedFallbackOnlyExamples,
                ["commandsUsingImportedScriptPlaybackMedia"] = commandsUsingImportedScriptPlaybackMedia,
                ["commandsUsingImportedScriptPlaybackMediaWithoutPoster"] = commandsUsingImportedScriptPlaybackMediaWithoutPoster,
                ["commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars"] = commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars,
                ["commandsUsingImportedScriptPlaybackMediaOversizedAssets"] = commandsUsingImportedScriptPlaybackMediaOversizedAssets,
                ["commandsUsingImportedScriptPlaybackMediaStaleAssets"] = commandsUsingImportedScriptPlaybackMediaStaleAssets
            }
        };
    }

    private static bool TypeHasRelatedContent(
        ApiTypeModel type,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap)
    {
        if (type is null || typeRelatedContentMap is null)
            return false;

        return typeRelatedContentMap.TryGetValue(type.FullName, out var relatedContent) &&
               relatedContent is not null &&
               relatedContent.HasEntries;
    }

    private static bool MemberHasRelatedContent(
        ApiMemberModel member,
        IReadOnlyDictionary<string, ApiTypeRelatedContentModel> typeRelatedContentMap)
    {
        if (member is null || typeRelatedContentMap is null)
            return false;

        foreach (var relatedContent in typeRelatedContentMap.Values)
        {
            if (relatedContent?.MemberEntries is null)
                continue;

            if (relatedContent.MemberEntries.TryGetValue(member, out var entries) && entries.Count > 0)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<ApiTypeModel> GetConfiguredQuickStartTypes(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options)
    {
        if (types is null || types.Count == 0 || options is null || options.QuickStartTypeNames.Count == 0)
            return Array.Empty<ApiTypeModel>();

        var results = new List<ApiTypeModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMainTypeMatches(results, seen, types, options.QuickStartTypeNames);
        return results;
    }

    private static void AppendSourceCoverageWarning(
        IReadOnlyList<SourceCoverageGroup> groups,
        Func<SourceCoverageGroup, int> countSelector,
        Func<SourceCoverageGroup, IReadOnlyList<string>> sampleSelector,
        string message,
        List<string> warnings)
    {
        if (groups is null || groups.Count == 0 || string.IsNullOrWhiteSpace(message) || warnings is null)
            return;

        var active = groups
            .Select(group => new
            {
                Group = group,
                Count = Math.Max(0, countSelector(group)),
                Samples = (sampleSelector(group) ?? Array.Empty<string>())
                    .Where(static sample => !string.IsNullOrWhiteSpace(sample))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(static entry => entry.Count > 0)
            .ToArray();
        if (active.Length == 0)
            return;

        var breakdown = string.Join(", ", active.Select(static entry => $"{entry.Group.Label} {entry.Count}"));
        var samples = active
            .SelectMany(static entry => entry.Samples)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var sampleText = samples.Length == 0
            ? string.Empty
            : $" (samples: {string.Join(" | ", samples)})";
        warnings.Add($"API docs source coverage: {message} ({breakdown}){sampleText}.");
    }

    private static bool HasOnlyGeneratedPowerShellFallbackExamples(ApiTypeModel type)
    {
        if (type is null || type.Examples.Count == 0)
            return false;

        var codeExamples = type.Examples.Where(IsCodeExample).ToArray();
        return codeExamples.Length > 0 &&
               codeExamples.All(static example =>
                   string.Equals(example.Origin, ApiExampleOrigins.GeneratedFallback, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCodeExamples(ApiTypeModel type)
    {
        return type is not null && type.Examples.Any(IsCodeExample);
    }

    private static bool HasCodeExamplesFromOrigin(ApiTypeModel type, string origin)
    {
        return type is not null &&
               !string.IsNullOrWhiteSpace(origin) &&
               type.Examples.Any(example =>
                   IsCodeExample(example) &&
                   string.Equals(example.Origin, origin, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasImportedPlaybackMedia(ApiTypeModel type)
    {
        return GetImportedPlaybackMediaExamples(type).Any();
    }

    private static bool HasImportedPlaybackMediaWithPoster(ApiTypeModel type)
    {
        var examples = GetImportedPlaybackMediaExamples(type).ToArray();
        return examples.Length > 0 &&
               examples.All(static example => !string.IsNullOrWhiteSpace(example.Media?.PosterUrl));
    }

    private static bool HasImportedPlaybackMediaWithoutPoster(ApiTypeModel type)
    {
        return GetImportedPlaybackMediaExamples(type)
            .Any(static example => string.IsNullOrWhiteSpace(example.Media?.PosterUrl));
    }

    private static bool HasImportedPlaybackMediaWithUnsupportedSidecars(ApiTypeModel type)
    {
        return GetImportedPlaybackMediaExamples(type)
            .Any(static example => example.Media?.HasUnsupportedSidecars == true);
    }

    private static bool HasImportedPlaybackMediaWithOversizedAssets(ApiTypeModel type)
    {
        return GetImportedPlaybackMediaExamples(type)
            .Any(static example => example.Media?.HasOversizedAssets == true);
    }

    private static bool HasImportedPlaybackMediaWithStaleAssets(ApiTypeModel type)
    {
        return GetImportedPlaybackMediaExamples(type)
            .Any(static example => example.Media?.HasStaleAssets == true);
    }

    private static IEnumerable<ApiExampleModel> GetImportedPlaybackMediaExamples(ApiTypeModel? type)
    {
        if (type?.Examples is null || type.Examples.Count == 0)
            return Enumerable.Empty<ApiExampleModel>();

        return type.Examples.Where(static example =>
            example is not null &&
            example.Kind.Equals("media", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(example.Origin, ApiExampleOrigins.ImportedScript, StringComparison.OrdinalIgnoreCase) &&
            example.Media is not null &&
            example.Media.Type.Equals("terminal", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(example.Media.MimeType, "application/x-asciicast", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCodeExample(ApiExampleModel example)
    {
        return example is not null &&
               example.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(example.Text);
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
                    if (stats.RepoMismatchUrlSamples.Length < sampleLimit)
                    {
                        var urlSample = url.Trim();
                        if (!string.IsNullOrWhiteSpace(urlSample))
                            stats.RepoMismatchUrlSamples = stats.RepoMismatchUrlSamples
                                .Append(urlSample)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Take(sampleLimit)
                                .ToArray();
                    }
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
        public string[] RepoMismatchUrlSamples { get; set; } = Array.Empty<string>();
    }

    private sealed class SourceCoverageGroup
    {
        public SourceCoverageGroup(string label, SourceCoverageStats stats)
        {
            Label = label;
            Stats = stats;
        }

        public string Label { get; }
        public SourceCoverageStats Stats { get; }
    }
}
