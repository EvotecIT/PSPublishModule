namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    // Declarations describe the requested coverage; they never grant publication to a result.
    private static void ApplyDefinitions(
        BenchmarkEvidenceCatalog catalog,
        IEnumerable<BenchmarkComparisonDefinition> comparisons)
    {
        var definitions = new Dictionary<string, BenchmarkComparisonDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkComparisonDefinition source in comparisons)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.ComparisonId) ||
                string.IsNullOrWhiteSpace(source.Area) || string.IsNullOrWhiteSpace(source.Title) ||
                source.Libraries is null || source.Libraries.Length == 0 ||
                source.Libraries.Any(string.IsNullOrWhiteSpace) ||
                source.ExpectedPlatforms is null || source.ExpectedPlatforms.Length == 0)
                throw new InvalidOperationException("Comparison declarations require an identifier, area, title, libraries and expected platforms.");

            var definition = new BenchmarkComparisonDefinition
            {
                ComparisonId = source.ComparisonId.Trim(),
                Area = source.Area.Trim().ToLowerInvariant(),
                Title = source.Title.Trim(),
                Libraries = source.Libraries.Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ExpectedPlatforms = NormalizeExpectedPlatforms(source.ExpectedPlatforms).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
            if (definitions.TryGetValue(definition.ComparisonId, out BenchmarkComparisonDefinition? existing))
            {
                if (!string.Equals(existing.Area, definition.Area, StringComparison.Ordinal) ||
                    !string.Equals(existing.Title, definition.Title, StringComparison.Ordinal) ||
                    !existing.Libraries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .SequenceEqual(definition.Libraries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) ||
                    !existing.ExpectedPlatforms.SequenceEqual(definition.ExpectedPlatforms, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Conflicting declarations for comparison '{definition.ComparisonId}'. Supply one authoritative comparison catalog.");
                continue;
            }
            definitions.Add(definition.ComparisonId, definition);
        }

        catalog.SchemaVersion = SupportedSchemaVersion;
        catalog.Comparisons = definitions.Values.OrderBy(value => value.ComparisonId, StringComparer.OrdinalIgnoreCase).ToArray();
        catalog.Areas = catalog.Comparisons.Select(value => value.Area).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        foreach (BenchmarkEvidenceEntry entry in catalog.Entries)
        {
            if (definitions.TryGetValue(entry.ComparisonId, out BenchmarkComparisonDefinition? definition))
                entry.Area = definition.Area;
        }

        // Preserve quick/full availability for measured lanes and add full-mode gaps for every declaration.
        var availability = BuildAvailability(catalog.Entries, catalog.ExpectedPlatforms).ToList();
        foreach (BenchmarkComparisonDefinition definition in catalog.Comparisons)
        {
            availability.RemoveAll(value => string.Equals(value.ComparisonId, definition.ComparisonId,
                StringComparison.OrdinalIgnoreCase) && !definition.ExpectedPlatforms.Contains(value.Platform, StringComparer.OrdinalIgnoreCase));
            foreach (string platform in definition.ExpectedPlatforms)
            {
                if (availability.Any(value => string.Equals(value.ComparisonId, definition.ComparisonId,
                        StringComparison.OrdinalIgnoreCase) && value.RunMode == "full" && value.Platform == platform))
                    continue;
                BenchmarkEvidenceEntry[] matches = catalog.Entries.Where(entry =>
                    string.Equals(entry.ComparisonId, definition.ComparisonId, StringComparison.OrdinalIgnoreCase) &&
                    entry.Platform == platform && entry.RunMode == "full" && entry.Publish).ToArray();
                availability.Add(new BenchmarkPlatformAvailability
                {
                    ComparisonId = definition.ComparisonId,
                    Platform = platform,
                    RunMode = "full",
                    Available = matches.Length > 0,
                    RunModes = matches.Length > 0 ? new[] { "full" } : Array.Empty<string>(),
                    LatestUtc = matches.Length > 0 ? matches.Max(value => value.GeneratedUtc) : null
                });
            }
        }
        catalog.Availability = availability.OrderBy(value => value.ComparisonId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.RunMode, StringComparer.Ordinal).ThenBy(value => value.Platform, StringComparer.Ordinal).ToArray();
    }
}
