using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

public sealed partial class BenchmarkComparisonViewBuilder
{
    /// <summary>
    /// Writes the view as an index plus one file per area, so a page can list every comparison and load an
    /// area's full data only when it is opened.
    /// </summary>
    /// <param name="view">Catalog view.</param>
    /// <param name="directory">Output directory.</param>
    /// <returns>The files written: <c>index.json</c> first, then one file per area.</returns>
    public static string[] WriteAreaFiles(BenchmarkComparisonCatalogView view, string directory)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Output directory is required.", nameof(directory));

        Directory.CreateDirectory(directory);
        var written = new List<string>();
        string indexPath = Path.Combine(directory, "index.json");
        BenchmarkJson.Write(indexPath, view.Index);
        written.Add(indexPath);
        foreach (BenchmarkAreaIndexView area in view.Index.Areas)
        {
            string areaPath = Path.Combine(directory, area.File);
            BenchmarkJson.Write(areaPath, new BenchmarkAreaView
            {
                Id = area.Id,
                Comparisons = view.Comparisons.Where(comparison => string.Equals(comparison.Area, area.Id, StringComparison.OrdinalIgnoreCase)).ToArray()
            });
            written.Add(areaPath);
        }
        return written.ToArray();
    }

    private static BenchmarkComparisonIndexView BuildIndex(string[] areas, BenchmarkComparisonView[] comparisons)
    {
        var ids = areas
            .Concat(comparisons.Select(comparison => comparison.Area))
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (comparisons.Any(comparison => string.IsNullOrWhiteSpace(comparison.Area)))
            ids.Add(string.Empty);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new BenchmarkComparisonIndexView
        {
            Areas = ids.Select(id =>
            {
                BenchmarkComparisonView[] inArea = comparisons
                    .Where(comparison => string.Equals(comparison.Area ?? string.Empty, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return new BenchmarkAreaIndexView
                {
                    Id = id,
                    File = UniqueAreaFile(id, files),
                    Groups = inArea.Select(comparison => comparison.Group).Where(group => group.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Measured = inArea.Count(comparison => comparison.Measured),
                    Comparisons = inArea.Select(comparison => new BenchmarkComparisonSummaryView
                    {
                        Id = comparison.Id,
                        Title = comparison.Title,
                        Group = comparison.Group,
                        Subject = comparison.Subject,
                        Libraries = comparison.Libraries,
                        Measured = comparison.Measured,
                        Eligibility = comparison.Eligibility,
                        Headlines = HeadlineRun(comparison)?.HeadToHead
                            .Select(item => item.Text)
                            .Where(text => text.Length > 0)
                            .ToArray() ?? Array.Empty<string>()
                    }).ToArray()
                };
            }).ToArray()
        };
    }

    // Headlines are public claims, so they come only from a published run: full over standard, then newest.
    private static BenchmarkRunView? HeadlineRun(BenchmarkComparisonView comparison)
        => comparison.Runs
            .Where(run => run.Published)
            .OrderBy(run => run.IsFull ? 0 : 1)
            .ThenByDescending(run => run.GeneratedUtc)
            .FirstOrDefault();

    // Area ids become file names: lower-case letters, digits and dashes, unique within the index.
    private static string UniqueAreaFile(string area, ISet<string> used)
    {
        var builder = new StringBuilder();
        foreach (char character in (area ?? string.Empty).ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(character) && character < 128 ? character : '-');
        string stem = builder.ToString().Trim('-');
        if (stem.Length == 0)
            stem = "other";
        string name = stem + ".json";
        for (int index = 2; !used.Add(name) || string.Equals(name, "index.json", StringComparison.OrdinalIgnoreCase); index++)
            name = stem + "-" + index.ToString(CultureInfo.InvariantCulture) + ".json";
        return name;
    }

    private static void AnnotateComparison(
        BenchmarkComparisonView view,
        BenchmarkComparisonDefinition definition,
        BenchmarkEvidenceCatalog catalog)
    {
        BenchmarkPublishPolicy policy = catalog.Policy ?? BenchmarkPublishPolicy.CreateDefault();
        view.Group = definition.Group ?? string.Empty;
        view.Frameworks = (definition.Frameworks ?? Array.Empty<string>()).ToArray();

        BenchmarkComparisonEligibility? eligibility = catalog.Eligibility.FirstOrDefault(value =>
            string.Equals(value.ComparisonId, definition.ComparisonId, StringComparison.OrdinalIgnoreCase));
        view.Eligibility = eligibility is null
            ? new BenchmarkEligibilityView { Publishable = false, Reason = "The comparison is not declared." }
            : new BenchmarkEligibilityView { Publishable = eligibility.Publishable, Reason = eligibility.Reason };

        view.Availability = catalog.Availability
            .Where(value => string.Equals(value.ComparisonId, definition.ComparisonId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => PlatformRank(value.Platform))
            .ThenBy(value => value.Framework, StringComparer.Ordinal)
            .Select(value =>
            {
                // Availability is public: it names only profiles the policy publishes, even when a catalog was
                // written by hand, and says "no published run" rather than naming unpublished ones.
                BenchmarkEvidenceProfile[] publishable = value.Profiles.Where(policy.PublishableProfiles.Contains).ToArray();
                bool available = value.Available && publishable.Length > 0;
                string[] profiles = publishable.Select(profile => BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.Format(profile)).ToArray();
                string date = available ? value.LatestUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
                bool measured = view.Runs.Any(run =>
                    string.Equals(run.Platform, value.Platform, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(run.Framework, value.Framework, StringComparison.OrdinalIgnoreCase));
                var parts = new List<string> { PlatformLabel(value.Platform) };
                if (value.Framework.Length > 0)
                    parts.Add(value.Framework);
                parts.Add(available ? string.Join(", ", profiles) + " · " + date : measured ? "no published run" : "not measured");
                return new BenchmarkAvailabilityView
                {
                    Platform = value.Platform,
                    PlatformLabel = PlatformLabel(value.Platform),
                    Framework = value.Framework,
                    Available = available,
                    Profiles = profiles,
                    LatestDate = date,
                    Text = string.Join(" · ", parts)
                };
            })
            .ToArray();

        view.LibraryStates = view.Libraries.Select(id =>
        {
            BenchmarkLibraryDeclaration? library = catalog.Libraries.FirstOrDefault(value =>
                string.Equals(value.Id, id, StringComparison.OrdinalIgnoreCase));
            bool published = library is not null && library.IsPublishableUnder(policy);
            return new BenchmarkLibraryStateView
            {
                Id = id,
                Restriction = library is null ? "unclassified" : BenchmarkDeclarationService.Format(library.Restriction),
                Published = published,
                Note = published ? string.Empty : library is null ? "not published (no library policy)" : "not published (licence)",
                Reason = library?.Reason ?? string.Empty,
                Version = library?.Version ?? string.Empty,
                License = library?.License ?? string.Empty
            };
        }).ToArray();
    }

    internal static BenchmarkIntervalView? DescribeInterval(BenchmarkConfidenceInterval? interval)
        => interval is null
            ? null
            : new BenchmarkIntervalView
            {
                Level = interval.Level,
                LowerMs = interval.LowerMs,
                UpperMs = interval.UpperMs,
                Text = (interval.Level * 100).ToString("0.#", CultureInfo.InvariantCulture) + "% CI " +
                       FormatTime(interval.LowerMs) + " – " + FormatTime(interval.UpperMs)
            };

    private static bool IsEven(BenchmarkConfidenceInterval? cell, BenchmarkConfidenceInterval? fastest)
        => cell is not null && fastest is not null && BenchmarkEvidenceIdentity.Overlaps(cell, fastest);

    internal static string DescribeProfile(BenchmarkEvidenceProfile profile, bool published) => profile switch
    {
        BenchmarkEvidenceProfile.Full => published ? "Full BenchmarkDotNet run" : "Full BenchmarkDotNet run (not published)",
        BenchmarkEvidenceProfile.Standard => published ? "Standard run" : "Standard run (not published)",
        _ => "Diagnostic run (not published)"
    };

    internal static BenchmarkRunnerView DescribeRunner(BenchmarkRunnerInfo? runner)
    {
        if (runner is null || string.IsNullOrWhiteSpace(runner.Kind))
            return new BenchmarkRunnerView();

        string kind = runner.Kind.Trim().ToLowerInvariant();
        string name = kind switch
        {
            "benchmarkdotnet" => "BenchmarkDotNet",
            "powershell-suite" => "PowerShell suite",
            "rotated" => "Rotated runner",
            "process" => "Process runner",
            _ => runner.Kind
        };
        var parts = new List<string> { string.IsNullOrWhiteSpace(runner.Version) ? name : name + " " + runner.Version };
        if (!string.IsNullOrWhiteSpace(runner.Statistic)) parts.Add(runner.Statistic);
        if (runner.Warmups is int warmups) parts.Add(Count(warmups, "warmup"));
        if (runner.Iterations is int iterations) parts.Add(Count(iterations, "iteration"));
        if (runner.Launches is int launches) parts.Add(Count(launches, "launch", "launches"));
        if (!string.IsNullOrWhiteSpace(runner.Ordering)) parts.Add(runner.Ordering);
        if (runner.InProcess is bool inProcess) parts.Add(inProcess ? "in process" : "separate processes");
        if (!string.IsNullOrWhiteSpace(runner.Affinity)) parts.Add("affinity " + runner.Affinity);
        if (!string.IsNullOrWhiteSpace(runner.Priority)) parts.Add("priority " + runner.Priority);

        return new BenchmarkRunnerView
        {
            Kind = kind,
            Version = runner.Version ?? string.Empty,
            Statistic = runner.Statistic ?? string.Empty,
            Warmups = runner.Warmups,
            Iterations = runner.Iterations,
            Launches = runner.Launches,
            Ordering = runner.Ordering ?? string.Empty,
            InProcess = runner.InProcess,
            Affinity = runner.Affinity ?? string.Empty,
            Priority = runner.Priority ?? string.Empty,
            Text = string.Join(" · ", parts)
        };
    }

    private static string Count(int value, string singular, string? plural = null)
        => value.ToString(CultureInfo.InvariantCulture) + " " + (value == 1 ? singular : plural ?? singular + "s");

    internal static BenchmarkNotPublishedView[] DescribeNotPublished(BenchmarkEvidenceEntry entry)
    {
        IEnumerable<BenchmarkEvidenceCell> left = entry.Cells.Where(cell =>
            !string.IsNullOrWhiteSpace(cell.Library) &&
            (IsLicence(cell) || (entry.Publish && !cell.Publishable)));
        return left
            .GroupBy(cell => cell.Library, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                bool licence = group.Any(IsLicence);
                string reason = licence
                    ? "not published (licence)"
                    : group.SelectMany(cell => cell.Issues).FirstOrDefault() ?? "not published";
                return new BenchmarkNotPublishedView
                {
                    Library = group.Key,
                    Licence = licence,
                    Text = reason,
                    Cells = group.Count()
                };
            })
            .OrderBy(value => value.Library, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLicence(BenchmarkEvidenceCell cell)
        => cell.Issues.Any(issue => issue.StartsWith(BenchmarkPublishPolicyEvaluator.NotPublishedLicence, StringComparison.Ordinal));
}

internal static class BenchmarkRunViewEvidence
{
    /// <summary>Adds the schema 5 lane facts to a run view.</summary>
    internal static BenchmarkRunView WithEvidence(this BenchmarkRunView run, BenchmarkEvidenceEntry entry, BenchmarkRunResult result)
    {
        run.Profile = BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.Format(entry.Profile);
        run.ProfileText = BenchmarkComparisonViewBuilder.DescribeProfile(entry.Profile, entry.Publish);
        run.Framework = entry.Framework ?? string.Empty;
        run.LaneParameters = new Dictionary<string, string>(entry.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        run.Runner = BenchmarkComparisonViewBuilder.DescribeRunner(string.IsNullOrWhiteSpace(entry.Runner?.Kind) ? result.Runner : entry.Runner);
        run.Completeness = entry.Completeness ?? string.Empty;
        run.Comparable = entry.Comparable;
        run.CompatibilityIssues = entry.CompatibilityIssues.ToArray();
        run.PolicyIssues = entry.PolicyIssues.ToArray();
        run.NotPublished = BenchmarkComparisonViewBuilder.DescribeNotPublished(entry);
        return run;
    }
}
