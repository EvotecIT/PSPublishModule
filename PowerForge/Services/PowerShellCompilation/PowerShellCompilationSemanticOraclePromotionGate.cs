using System.Collections.ObjectModel;

namespace PowerForge;

/// <summary>Fail-closed promotion checks for semantic observations produced by exact external hosts and CLR surfaces.</summary>
public static class PowerShellCompilationSemanticOraclePromotionGate
{
    /// <summary>
    /// Verifies provenance, exact host pins, multiple execution surfaces, and every semantic difference before a feature is promoted.
    /// </summary>
    public static IReadOnlyList<PowerShellCompilationSemanticOracleDifference> EnsurePromotable(
        string featureId,
        IReadOnlyList<PowerShellCompilationSemanticOracleEnvelope> observations,
        IReadOnlyDictionary<string, string> expectedHostArtifacts,
        IEnumerable<string>? allowedDifferencePaths = null,
        string differenceJustification = "")
    {
        if (string.IsNullOrWhiteSpace(featureId))
            throw new ArgumentException("A semantic feature identity is required.", nameof(featureId));
        if (observations is null) throw new ArgumentNullException(nameof(observations));
        if (expectedHostArtifacts is null) throw new ArgumentNullException(nameof(expectedHostArtifacts));
        if (observations.Count < 2)
            throw new InvalidOperationException("Semantic promotion requires at least two independent observations.");

        var feature = featureId.Trim();
        var knownProfiles = PowerShellCompilationSemanticOracleCatalog.Profiles
            .Select(static profile => profile.ProfileId)
            .ToHashSet(StringComparer.Ordinal);
        var unknownPin = expectedHostArtifacts.Keys.FirstOrDefault(profileId => !knownProfiles.Contains(profileId));
        if (unknownPin is not null)
            throw new KeyNotFoundException($"Unknown semantic profile host-artifact pin '{unknownPin}'.");

        var surfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hostBackedProfiles = new HashSet<string>(StringComparer.Ordinal);
        var runtimeFreeProfiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (observation is null) throw new ArgumentException("Semantic observations cannot contain null entries.", nameof(observations));
            if (observation.SchemaVersion != 2)
                throw new InvalidOperationException($"Semantic promotion requires envelope schema 2, not {observation.SchemaVersion}.");
            var profile = PowerShellCompilationSemanticOracleCatalog.Get(observation.ProfileId);
            if (!PowerShellCompilationSemanticOracleCatalog.FeatureProvenance.Any(evidence =>
                    evidence.FeatureId.Equals(feature, StringComparison.Ordinal) &&
                    evidence.ProfileId.Equals(observation.ProfileId, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Semantic feature '{feature}' has no pinned provenance for profile '{observation.ProfileId}'.");
            if (string.IsNullOrWhiteSpace(observation.ExecutionSurface))
                throw new InvalidOperationException("Semantic promotion observations require an execution-surface identity.");
            if (!Enum.TryParse<PowerShellCompilationSemanticExecutionSurface>(observation.ExecutionSurface.Trim(), ignoreCase: true, out var surface) ||
                !Enum.IsDefined(typeof(PowerShellCompilationSemanticExecutionSurface), surface))
                throw new InvalidOperationException($"Unknown semantic execution surface '{observation.ExecutionSurface}'.");
            surfaces.Add(observation.ProfileId + "|" + surface);

            var isRuntimeFree = surface == PowerShellCompilationSemanticExecutionSurface.Strict ||
                                surface == PowerShellCompilationSemanticExecutionSurface.HandWrittenClr;
            if (isRuntimeFree)
            {
                if (observation.HostArtifact is not null)
                    throw new InvalidOperationException($"Runtime-free observation '{surface}' must not carry a PowerShell host artifact.");
                runtimeFreeProfiles.Add(observation.ProfileId);
                continue;
            }
            var artifact = PowerShellCompilationSemanticHostArtifactService.Normalize(observation.HostArtifact
                ?? throw new InvalidOperationException($"Host-backed observation '{surface}' is missing its exact host artifact."));
            PowerShellCompilationSemanticHostArtifactService.EnsureMatchesProfile(artifact, profile);
            if (!expectedHostArtifacts.TryGetValue(observation.ProfileId, out var expected))
                throw new InvalidOperationException($"Semantic promotion is missing the recorded host-artifact pin for profile '{observation.ProfileId}'.");
            var normalizedExpected = NormalizeSha256(expected, observation.ProfileId);
            if (!artifact.IdentitySha256.Equals(normalizedExpected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Observed host artifact '{artifact.IdentitySha256}' does not match the promoted pin '{normalizedExpected}' for profile '{observation.ProfileId}'.");
            hostBackedProfiles.Add(observation.ProfileId);
        }
        if (surfaces.Count < 2)
            throw new InvalidOperationException("Semantic promotion requires at least two distinct profile/execution-surface observations.");
        if (!hostBackedProfiles.Overlaps(runtimeFreeProfiles))
            throw new InvalidOperationException("Semantic promotion requires a pinned host-backed observation and a runtime-free CLR observation for the same semantic profile.");

        var allowed = (allowedDifferencePaths ?? Array.Empty<string>())
            .Select(static path => path?.Trim() ?? string.Empty)
            .Where(static path => path.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var differences = new List<PowerShellCompilationSemanticOracleDifference>();
        var baseline = observations[0];
        for (var index = 1; index < observations.Count; index++)
            differences.AddRange(PowerShellCompilationSemanticOracleComparer.Compare(baseline, observations[index]));

        var unexplained = differences.FirstOrDefault(difference => !allowed.Contains(difference.Path));
        if (unexplained is not null)
            throw new InvalidOperationException($"Semantic feature '{feature}' has an unexplained difference at '{unexplained.Path}'.");
        if (differences.Count > 0 && string.IsNullOrWhiteSpace(differenceJustification))
            throw new InvalidOperationException($"Semantic feature '{feature}' has allowed differences but no recorded justification.");
        return new ReadOnlyCollection<PowerShellCompilationSemanticOracleDifference>(differences);
    }

    private static string NormalizeSha256(string value, string profileId)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
            throw new ArgumentException($"Host-artifact pin for profile '{profileId}' must be a 64-character hexadecimal SHA-256 value.", nameof(value));
        return normalized;
    }
}
