namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    // Declarations describe the requested coverage; they never grant publication to a result.
    private static void ApplyDefinitions(BenchmarkEvidenceCatalog catalog)
    {
        var definitions = new Dictionary<string, BenchmarkComparisonDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkComparisonDefinition source in catalog.Comparisons ?? Array.Empty<BenchmarkComparisonDefinition>())
        {
            BenchmarkComparisonDefinition definition = NormalizeDefinition(source);
            if (definitions.TryGetValue(definition.ComparisonId, out BenchmarkComparisonDefinition? existing))
            {
                if (BenchmarkJson.ComputeSha256(existing) != BenchmarkJson.ComputeSha256(definition))
                    throw new InvalidOperationException($"Conflicting declarations for comparison '{definition.ComparisonId}'. Supply one authoritative declaration document.");
                continue;
            }
            definitions.Add(definition.ComparisonId, definition);
        }

        BenchmarkPublishPolicy policy = catalog.Policy ??= BenchmarkPublishPolicy.CreateDefault();
        var declarationService = new BenchmarkDeclarationService();
        catalog.SchemaVersion = CurrentSchemaVersion;
        catalog.Comparisons = definitions.Values.OrderBy(value => value.ComparisonId, StringComparer.OrdinalIgnoreCase).ToArray();
        catalog.Areas = catalog.Comparisons.Select(value => value.Area).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        catalog.Eligibility = catalog.Comparisons.Select(definition =>
        {
            BenchmarkEligibility eligibility = declarationService.DeriveEligibility(definition, catalog.Libraries, policy);
            return new BenchmarkComparisonEligibility
            {
                ComparisonId = definition.ComparisonId,
                Publishable = eligibility.Publishable,
                Reason = eligibility.Reason
            };
        }).ToArray();
        foreach (BenchmarkEvidenceEntry entry in catalog.Entries)
        {
            if (definitions.TryGetValue(entry.ComparisonId, out BenchmarkComparisonDefinition? definition))
                entry.Area = definition.Area;
        }

        catalog.Availability = BuildAvailability(catalog);
    }

    private static BenchmarkComparisonDefinition NormalizeDefinition(BenchmarkComparisonDefinition source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.ComparisonId) ||
            string.IsNullOrWhiteSpace(source.Area) || string.IsNullOrWhiteSpace(source.Title) ||
            source.Libraries is null || source.Libraries.Length == 0 ||
            source.Libraries.Any(string.IsNullOrWhiteSpace) ||
            source.ExpectedPlatforms is null || source.ExpectedPlatforms.Length == 0)
            throw new InvalidOperationException("Comparison declarations require an identifier, area, title, libraries and expected platforms.");

        return new BenchmarkComparisonDefinition
        {
            ComparisonId = source.ComparisonId.Trim(),
            Area = source.Area.Trim().ToLowerInvariant(),
            Group = source.Group?.Trim() ?? string.Empty,
            Title = source.Title.Trim(),
            Subject = source.Subject?.Trim() ?? string.Empty,
            Libraries = source.Libraries.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Axes = (source.Axes ?? Array.Empty<BenchmarkComparisonAxis>())
                .Select(axis => new BenchmarkComparisonAxis { Name = axis.Name.Trim(), Values = NormalizeAxisValues(source.ComparisonId, axis) })
                .ToArray(),
            Cases = (source.Cases ?? Array.Empty<string>()).Select(value => value.Trim()).ToArray(),
            Cells = (source.Cells ?? Array.Empty<BenchmarkExpectedCell>())
                .Where(cell => cell is not null)
                .Select(cell => new BenchmarkExpectedCell
                {
                    Case = cell.Case?.Trim() ?? string.Empty,
                    Library = cell.Library?.Trim() ?? string.Empty,
                    Parameters = NormalizeCellParameters(source.ComparisonId, cell.Parameters)
                })
                .ToArray(),
            ExpectedPlatforms = NormalizeExpectedPlatforms(source.ExpectedPlatforms).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Frameworks = (source.Frameworks ?? Array.Empty<string>()).Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Eligibility = new BenchmarkEligibility
            {
                Publishable = source.Eligibility?.Publishable ?? true,
                Reason = source.Eligibility?.Reason?.Trim() ?? string.Empty
            }
        };
    }

    // Parameter names ignore case, so a cell that gives one twice (Scale and scale) is rejected with a clear message on
    // every path, including catalogs merged without their declaration document.
    private static Dictionary<string, string> NormalizeCellParameters(string comparisonId, IDictionary<string, string>? parameters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in parameters ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                continue;
            string name = pair.Key.Trim();
            if (normalized.ContainsKey(name))
                throw new InvalidOperationException($"Comparison '{comparisonId.Trim()}' has a cell that gives parameter '{name}' more than once; parameter names ignore case.");
            if (string.IsNullOrWhiteSpace(pair.Value))
                throw new InvalidOperationException($"Comparison '{comparisonId.Trim()}' has a cell with a blank value for parameter '{name}'.");
            normalized[name] = pair.Value.Trim();
        }
        return normalized;
    }

    // A blank axis value can't be told apart from a missing parameter, so it is rejected on every path, including
    // catalogs merged without their declaration document.
    private static string[] NormalizeAxisValues(string comparisonId, BenchmarkComparisonAxis axis)
    {
        string[] values = (axis.Values ?? Array.Empty<string>()).Select(value => value?.Trim() ?? string.Empty).ToArray();
        if (values.Any(value => value.Length == 0))
            throw new InvalidOperationException($"Comparison '{comparisonId.Trim()}' axis '{axis.Name.Trim()}' has a blank value.");
        return values;
    }

    private static BenchmarkPlatformAvailability[] BuildAvailability(BenchmarkEvidenceCatalog catalog)
    {
        var keys = new List<(string ComparisonId, string Framework, string Platform)>();
        foreach (BenchmarkComparisonDefinition definition in catalog.Comparisons)
        {
            string[] measuredFrameworks = catalog.Entries
                .Where(entry => string.Equals(entry.ComparisonId, definition.ComparisonId, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Framework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] frameworks = definition.Frameworks.Length > 0 ? definition.Frameworks
                : measuredFrameworks.Length > 0 ? measuredFrameworks
                : new[] { string.Empty };
            foreach (string framework in frameworks)
                foreach (string platform in definition.ExpectedPlatforms)
                    keys.Add((definition.ComparisonId, framework, platform));
        }
        foreach (BenchmarkEvidenceEntry entry in catalog.Entries)
        {
            if (!catalog.Comparisons.Any(definition => string.Equals(definition.ComparisonId, entry.ComparisonId, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (string platform in catalog.ExpectedPlatforms)
                    keys.Add((entry.ComparisonId, entry.Framework, platform));
            }
        }

        return keys
            .Distinct()
            .Select(key =>
            {
                BenchmarkEvidenceEntry[] published = catalog.Entries.Where(entry =>
                        entry.Publish &&
                        string.Equals(entry.ComparisonId, key.ComparisonId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.Framework, key.Framework, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.Platform, key.Platform, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return new BenchmarkPlatformAvailability
                {
                    ComparisonId = key.ComparisonId,
                    Framework = key.Framework,
                    Platform = key.Platform,
                    Available = published.Length > 0,
                    Profiles = published.Select(entry => entry.Profile).Distinct().OrderBy(value => value).ToArray(),
                    LatestUtc = published.Length > 0 ? published.Max(entry => entry.GeneratedUtc) : null
                };
            })
            .OrderBy(value => value.ComparisonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Framework, StringComparer.Ordinal)
            .ThenBy(value => value.Platform, StringComparer.Ordinal)
            .ToArray();
    }
}
