using System.Globalization;

namespace PowerForge;

/// <summary>Lane being evaluated against the publish policy.</summary>
internal sealed class BenchmarkPolicyLane
{
    internal BenchmarkRunResult Result { get; set; } = new();
    internal string ComparisonId { get; set; } = string.Empty;
    internal string Framework { get; set; } = string.Empty;
    internal BenchmarkEvidenceProfile Profile { get; set; }
    internal bool Publish { get; set; }
    internal BenchmarkComparisonDefinition? Definition { get; set; }
    internal IReadOnlyCollection<BenchmarkLibraryDeclaration> Libraries { get; set; } = Array.Empty<BenchmarkLibraryDeclaration>();
    internal BenchmarkPublishPolicy Policy { get; set; } = BenchmarkPublishPolicy.CreateDefault();
}

/// <summary>Outcome of a publish policy evaluation.</summary>
internal sealed class BenchmarkPolicyEvaluation
{
    internal BenchmarkEvidenceCell[] Cells { get; set; } = Array.Empty<BenchmarkEvidenceCell>();
    internal BenchmarkRunnerInfo Runner { get; set; } = new();
    internal BenchmarkEvidenceProvenance Provenance { get; set; } = new();
    internal string[] LaneIssues { get; set; } = Array.Empty<string>();
    internal string Completeness { get; set; } = string.Empty;
}

/// <summary>
/// Applies <see cref="BenchmarkPublishPolicy"/> to one lane: lane-level rules decide whether the lane may be
/// published, cell-level rules decide which cells appear in public claims.
/// </summary>
internal static class BenchmarkPublishPolicyEvaluator
{
    private static readonly string[] MachineIdentityKeys = { "machine", "machineName", "user", "userName" };

    internal static BenchmarkPolicyEvaluation Evaluate(BenchmarkPolicyLane lane)
    {
        BenchmarkPublishPolicy policy = lane.Policy;
        BenchmarkRunResult result = lane.Result;
        BenchmarkRunnerInfo runner = CopyRunner(result.Runner);
        BenchmarkEvidenceProvenance provenance = BuildProvenance(result);
        BenchmarkEvidenceCell[] cells = BenchmarkEvidenceCellBuilder.Build(result, lane.ComparisonId, policy);

        var laneIssues = new List<string>();
        if (lane.Publish)
        {
            AddProfileIssues(lane, runner, laneIssues);
            AddProvenanceIssues(lane, result, provenance, laneIssues);
            AddDeclarationIssues(lane, cells, provenance, laneIssues);
        }

        foreach (BenchmarkEvidenceCell cell in cells)
            cell.Issues = EvaluateCell(cell, lane).ToArray();

        if (lane.Publish && laneIssues.Count == 0)
        {
            foreach (BenchmarkEvidenceCell cell in cells)
                cell.Publishable = cell.Issues.Length == 0;
            if (!cells.Any(cell => cell.Publishable))
                laneIssues.Add("No cell meets the publish policy: " + string.Join(" ", cells.SelectMany(cell => cell.Issues).Distinct(StringComparer.Ordinal).Take(5)));
        }

        string completeness = string.Empty;
        if (lane.Publish && laneIssues.Count == 0)
        {
            ApplyOutcomes(cells);
            completeness = ResolveCompleteness(cells, lane);
        }
        else
        {
            foreach (BenchmarkEvidenceCell cell in cells)
                cell.Publishable = false;
        }

        return new BenchmarkPolicyEvaluation
        {
            Cells = cells,
            Runner = runner,
            Provenance = provenance,
            LaneIssues = laneIssues.ToArray(),
            Completeness = completeness
        };
    }

    private static void AddProfileIssues(BenchmarkPolicyLane lane, BenchmarkRunnerInfo runner, List<string> issues)
    {
        BenchmarkPublishPolicy policy = lane.Policy;
        string profile = BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.Format(lane.Profile);
        if (!policy.PublishableProfiles.Contains(lane.Profile))
            issues.Add($"The {profile} profile is never published.");
        if (string.IsNullOrWhiteSpace(runner.Kind))
            issues.Add("The result does not record its runner block (kind, statistic, warmups, iterations, ordering).");
        else if (lane.Profile == BenchmarkEvidenceProfile.Full &&
                 !policy.FullProfileRunnerKinds.Contains(runner.Kind, StringComparer.OrdinalIgnoreCase))
            issues.Add($"The full profile requires a {string.Join(" or ", policy.FullProfileRunnerKinds)} runner; this result came from '{runner.Kind}'.");
        if (!string.IsNullOrWhiteSpace(runner.Kind) &&
            !string.Equals(runner.Statistic, "median", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runner.Statistic, "mean", StringComparison.OrdinalIgnoreCase))
            issues.Add("The runner block must name its statistic: median or mean.");
        if (!string.IsNullOrWhiteSpace(runner.Kind) && (runner.Warmups ?? -1) < policy.MinimumWarmups)
            issues.Add($"Published evidence needs at least {policy.MinimumWarmups} recorded warmups per cell; the runner recorded {Describe(runner.Warmups)}.");
    }

    private static void AddProvenanceIssues(
        BenchmarkPolicyLane lane,
        BenchmarkRunResult result,
        BenchmarkEvidenceProvenance provenance,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(provenance.Os))
            issues.Add("Published evidence needs the operating-system description.");
        if (string.IsNullOrWhiteSpace(provenance.Architecture))
            issues.Add("Published evidence needs the process architecture.");
        if (provenance.Cores is null or <= 0)
            issues.Add("Published evidence needs the logical core count.");
        if (string.IsNullOrWhiteSpace(provenance.Sdk) && lane.Framework.Length > 0)
            issues.Add($"Published {lane.Framework} evidence needs the .NET SDK version.");
        if (!string.IsNullOrWhiteSpace(result.Environment.MachineName) ||
            MachineIdentityKeys.Any(key => result.Metadata.Keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))))
            issues.Add("Published evidence must not carry a machine or user name. Remove Environment.MachineName and the machine/user metadata before publishing.");
    }

    private static void AddDeclarationIssues(
        BenchmarkPolicyLane lane,
        BenchmarkEvidenceCell[] cells,
        BenchmarkEvidenceProvenance provenance,
        List<string> issues)
    {
        BenchmarkComparisonDefinition? definition = lane.Definition;
        if (definition is null)
        {
            if (lane.Policy.RequireDeclaration)
                issues.Add($"Comparison '{lane.ComparisonId}' is not declared. Published lanes need a comparison declaration and library policy.");
            return;
        }

        BenchmarkEligibility eligibility = new BenchmarkDeclarationService().DeriveEligibility(definition, lane.Libraries, lane.Policy);
        if (!eligibility.Publishable)
            issues.Add($"Comparison '{definition.ComparisonId}' is not publishable: {eligibility.Reason}");
        if (definition.Frameworks.Length > 0 && !definition.Frameworks.Contains(lane.Framework, StringComparer.OrdinalIgnoreCase))
            issues.Add($"Framework '{lane.Framework}' is not declared for comparison '{definition.ComparisonId}'.");
        foreach (BenchmarkDeclarationIssue drift in BenchmarkDeclarationService.CheckDrift(definition, lane.Libraries, cells))
            issues.Add("Declaration drift: " + drift.Message);

        if (!lane.Policy.RequirePackageHashes)
            return;
        foreach (string libraryId in cells.Select(cell => cell.Library)
                     .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, definition.Subject, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            BenchmarkLibraryDeclaration? library = lane.Libraries.FirstOrDefault(value => string.Equals(value.Id, libraryId, StringComparison.OrdinalIgnoreCase));
            if (library is null || !lane.Policy.PublishableRestrictions.Contains(library.Restriction))
                continue;
            BenchmarkPackageReference? package = provenance.Packages.FirstOrDefault(value =>
                string.Equals(value.Name, library.Package, StringComparison.OrdinalIgnoreCase));
            if (package is null || string.IsNullOrWhiteSpace(package.Version) || string.IsNullOrWhiteSpace(package.Sha256))
                issues.Add($"Library '{libraryId}' needs its package '{library.Package}' version and hash in metadata 'benchmark.package.{library.Package.ToLowerInvariant()}' and '.sha256'.");
            else if (!string.Equals(package.Version, library.Version, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Library '{libraryId}' ran package version {package.Version} but the library policy pins {library.Version}.");
        }
    }

    private static IEnumerable<string> EvaluateCell(BenchmarkEvidenceCell cell, BenchmarkPolicyLane lane)
    {
        BenchmarkPublishPolicy policy = lane.Policy;
        if (!string.Equals(cell.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            yield return string.IsNullOrWhiteSpace(cell.Reason) ? $"Cell {cell.Status.ToLowerInvariant()}." : $"Cell {cell.Status.ToLowerInvariant()}: {cell.Reason}";
        if (string.IsNullOrWhiteSpace(cell.Library))
        {
            yield return "Cell has no library identity.";
        }
        else
        {
            BenchmarkLibraryDeclaration? library = lane.Libraries.FirstOrDefault(value =>
                string.Equals(value.Id, cell.Library, StringComparison.OrdinalIgnoreCase));
            if (library is null)
            {
                if (policy.RequireDeclaration)
                    yield return $"Library '{cell.Library}' is not in the library policy.";
            }
            else
            {
                if (!policy.PublishableRestrictions.Contains(library.Restriction))
                    yield return $"Library '{library.Id}' is {BenchmarkKebabCaseEnumConverter<BenchmarkLibraryRestriction>.Format(library.Restriction)}: {library.Reason}";
                if (library.Restriction == BenchmarkLibraryRestriction.AcceptanceRequired &&
                    !lane.Result.AcceptedLibraries.Contains(library.Id, StringComparer.OrdinalIgnoreCase))
                    yield return $"Library '{library.Id}' ran without a recorded licence acceptance.";
            }
        }
        if (cell.FailureCount > 0 && string.Equals(cell.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            yield return $"Cell had {cell.FailureCount} failed sample(s); an unstable cell is not published.";
        if (cell.SampleCount < policy.MinimumMeasuredSamples)
            yield return $"Cell has {cell.SampleCount} measured samples; at least {policy.MinimumMeasuredSamples} are required.";
        if (cell.ConfidenceInterval is null)
            yield return "Cell has no confidence interval for its statistic.";
        else if (cell.ConfidenceInterval.Level + 1e-9 < policy.ConfidenceLevel)
            yield return $"Cell confidence interval level {cell.ConfidenceInterval.Level.ToString("0.###", CultureInfo.InvariantCulture)} is below {policy.ConfidenceLevel.ToString("0.###", CultureInfo.InvariantCulture)}.";
        if (policy.RequireOutputValidation && !string.Equals(cell.Validation, "passed", StringComparison.OrdinalIgnoreCase))
            yield return cell.Validation == "failed"
                ? $"Output validation failed: {cell.ValidationDetail}"
                : "Output validation was not recorded for this library and case.";
    }

    private static void ApplyOutcomes(BenchmarkEvidenceCell[] cells)
    {
        foreach (IGrouping<string, BenchmarkEvidenceCell> group in cells
                     .Where(cell => cell.Publishable && cell.ValueMs.HasValue && cell.ConfidenceInterval is not null)
                     .GroupBy(cell => string.Join("\u001f", cell.Case, cell.Operation, BenchmarkEvidenceCellBuilder.FormatParameters(cell.Parameters)), StringComparer.OrdinalIgnoreCase))
        {
            BenchmarkEvidenceCell fastest = group.OrderBy(cell => cell.ValueMs!.Value).First();
            foreach (BenchmarkEvidenceCell cell in group)
            {
                cell.Outcome = ReferenceEquals(cell, fastest) ? "fastest"
                    : BenchmarkEvidenceIdentity.Overlaps(cell.ConfidenceInterval!, fastest.ConfidenceInterval!) ? "even"
                    : "slower";
            }
        }
    }

    private static string ResolveCompleteness(BenchmarkEvidenceCell[] cells, BenchmarkPolicyLane lane)
    {
        string[] expectedLibraries = lane.Definition is null
            ? cells.Select(cell => cell.Library).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : lane.Definition.Libraries.Where(id => lane.Libraries.Any(library =>
                    string.Equals(library.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    lane.Policy.PublishableRestrictions.Contains(library.Restriction)))
                .ToArray();
        bool complete = cells
            .GroupBy(cell => string.Join("\u001f", cell.Case, cell.Operation, BenchmarkEvidenceCellBuilder.FormatParameters(cell.Parameters)), StringComparer.OrdinalIgnoreCase)
            .All(group => expectedLibraries.All(library => group.Any(cell =>
                cell.Publishable && string.Equals(cell.Library, library, StringComparison.OrdinalIgnoreCase))));
        return complete ? "complete" : "partial";
    }

    internal static BenchmarkEvidenceProvenance BuildProvenance(BenchmarkRunResult result)
    {
        var packages = new List<BenchmarkPackageReference>();
        foreach (KeyValuePair<string, string> item in result.Metadata)
        {
            const string prefix = "benchmark.package.";
            if (!item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                item.Key.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                continue;
            string name = item.Key.Substring(prefix.Length);
            result.Metadata.TryGetValue(prefix + name + ".sha256", out string? hash);
            packages.Add(new BenchmarkPackageReference { Name = name, Version = item.Value ?? string.Empty, Sha256 = hash ?? string.Empty });
        }

        result.Metadata.TryGetValue("gitSha", out string? gitSha);
        result.Metadata.TryGetValue("gitWorktreeClean", out string? clean);
        BenchmarkEnvironmentInfo environment = result.Environment;
        return new BenchmarkEvidenceProvenance
        {
            GitSha = gitSha ?? string.Empty,
            GitWorktreeClean = string.Equals(clean, "true", StringComparison.OrdinalIgnoreCase),
            Os = environment.OsDescription,
            Architecture = environment.ProcessArchitecture,
            Cpu = environment.ProcessorName,
            Cores = environment.LogicalCoreCount,
            Runtime = environment.RuntimeVersion,
            Sdk = environment.DotNetSdkVersion,
            Packages = packages.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static BenchmarkRunnerInfo CopyRunner(BenchmarkRunnerInfo? runner)
        => runner is null ? new BenchmarkRunnerInfo() : new BenchmarkRunnerInfo
        {
            Kind = runner.Kind?.Trim().ToLowerInvariant() ?? string.Empty,
            Version = runner.Version ?? string.Empty,
            Statistic = runner.Statistic?.Trim().ToLowerInvariant() ?? string.Empty,
            Warmups = runner.Warmups,
            Iterations = runner.Iterations,
            Launches = runner.Launches,
            Ordering = runner.Ordering ?? string.Empty,
            InProcess = runner.InProcess,
            Affinity = runner.Affinity ?? string.Empty,
            Priority = runner.Priority ?? string.Empty
        };

    private static string Describe(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";
}
