using System.Collections.ObjectModel;
using System.Text.Json;

namespace PowerForge;

/// <summary>Canonical reviewed exact-host pins used by semantic replay and feature promotion.</summary>
public static class PowerShellCompilationSemanticHostArtifactPinCatalog
{
    private const string ResourceName = "PowerForge.PowerShellCompilation.SemanticOracle.HostArtifactPins.json";
    private static readonly IReadOnlyDictionary<string, PowerShellCompilationSemanticHostArtifactPin> KnownPins = Load();

    /// <summary>All reviewed host pins, ordered by semantic profile identity.</summary>
    public static IReadOnlyList<PowerShellCompilationSemanticHostArtifactPin> Pins { get; } =
        new ReadOnlyCollection<PowerShellCompilationSemanticHostArtifactPin>(KnownPins.Values
            .OrderBy(static pin => pin.ProfileId, StringComparer.Ordinal)
            .ToArray());

    /// <summary>Exact accepted host-artifact identity for each promoted profile.</summary>
    public static IReadOnlyDictionary<string, string> ExpectedHostArtifactIdentities { get; } =
        new ReadOnlyDictionary<string, string>(KnownPins.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.HostArtifactIdentitySha256,
            StringComparer.Ordinal));

    /// <summary>Returns the reviewed exact-host pin for one promoted profile.</summary>
    public static PowerShellCompilationSemanticHostArtifactPin Get(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("A semantic profile identity is required.", nameof(profileId));
        return KnownPins.TryGetValue(profileId.Trim(), out var pin)
            ? pin
            : throw new KeyNotFoundException($"No reviewed semantic host-artifact pin exists for profile '{profileId}'.");
    }

    private static IReadOnlyDictionary<string, PowerShellCompilationSemanticHostArtifactPin> Load()
    {
        using var stream = typeof(PowerShellCompilationSemanticHostArtifactPinCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded semantic host-pin resource '{ResourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        if (schemaVersion != 1)
            throw new InvalidOperationException($"Unsupported semantic host-pin schema {schemaVersion}.");

        var result = new Dictionary<string, PowerShellCompilationSemanticHostArtifactPin>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("pins").EnumerateArray())
        {
            var profileId = GetString(item, "profileId");
            var pin = new PowerShellCompilationSemanticHostArtifactPin(
                profileId,
                GetString(item, "releaseIdentity"),
                GetString(item, "releaseTag"),
                GetString(item, "trackedTagPrefix"),
                GetString(item, "upstreamCommit"),
                GetString(item, "releaseAssetUri"),
                GetString(item, "releaseAssetSha256"),
                ReadHostArtifact(item.GetProperty("hostArtifact")),
                ReadStrings(item.GetProperty("reviewedCaseIds")));
            if (result.ContainsKey(pin.ProfileId))
                throw new InvalidOperationException($"Duplicate semantic host pin '{pin.ProfileId}'.");
            result.Add(pin.ProfileId, pin);
        }

        var profiles = PowerShellCompilationSemanticOracleCatalog.Profiles;
        var missing = profiles.FirstOrDefault(profile => !result.ContainsKey(profile.ProfileId));
        if (missing is not null)
            throw new InvalidOperationException($"Promoted semantic profile '{missing.ProfileId}' has no reviewed exact-host pin.");
        var unknown = result.Keys.FirstOrDefault(profileId => profiles.All(profile => profile.ProfileId != profileId));
        if (unknown is not null)
            throw new InvalidOperationException($"Semantic host pin '{unknown}' does not map to a promoted profile.");

        foreach (var profile in profiles)
        {
            var pin = result[profile.ProfileId];
            var artifact = pin.GetHostArtifact();
            PowerShellCompilationSemanticHostArtifactService.EnsureMatchesProfile(artifact, profile, artifact.Culture);
            if (!string.Equals(profile.UpstreamCommit, pin.UpstreamCommit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Semantic host pin '{pin.ProfileId}' does not match its profile's upstream commit.");
            var expectedCases = PowerShellCompilationSemanticOracleCaseCatalog.Cases
                .Where(item => item.ProfileIds.Contains(profile.ProfileId))
                .Select(static item => item.CaseId)
                .OrderBy(static caseId => caseId, StringComparer.Ordinal)
                .ToArray();
            if (!pin.ReviewedCaseIds.SequenceEqual(expectedCases, StringComparer.Ordinal))
                throw new InvalidOperationException($"Semantic host pin '{pin.ProfileId}' does not cover the complete promoted case set.");
            if (profile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7 &&
                (pin.ReleaseTag.Length == 0 || pin.TrackedTagPrefix.Length == 0 || pin.ReleaseAssetUri.Length == 0))
                throw new InvalidOperationException($"PowerShell 7 host pin '{pin.ProfileId}' requires release, tracking, and asset evidence.");
        }
        return new ReadOnlyDictionary<string, PowerShellCompilationSemanticHostArtifactPin>(result);
    }

    private static PowerShellCompilationSemanticHostArtifact ReadHostArtifact(JsonElement item)
        => new()
        {
            SchemaVersion = item.GetProperty("schemaVersion").GetInt32(),
            ExecutableName = GetString(item, "executableName"),
            ExecutableSha256 = GetString(item, "executableSha256"),
            ExecutableLength = item.GetProperty("executableLength").GetInt64(),
            ExecutableFileVersion = GetString(item, "executableFileVersion"),
            ExecutableProductVersion = GetString(item, "executableProductVersion"),
            HostVersion = GetString(item, "hostVersion"),
            BuildVersion = GetString(item, "buildVersion"),
            GitCommitId = GetString(item, "gitCommitId"),
            PowerShellEdition = GetString(item, "powerShellEdition"),
            OperatingSystem = GetString(item, "operatingSystem"),
            OperatingSystemVersion = GetString(item, "operatingSystemVersion"),
            Architecture = GetString(item, "architecture"),
            Culture = GetString(item, "culture"),
            UICulture = GetString(item, "uiCulture"),
            FeatureSwitches = ReadStrings(item.GetProperty("featureSwitches")),
            IdentitySha256 = GetString(item, "identitySha256")
        };

    private static string[] ReadStrings(JsonElement values)
        => values.EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();

    private static string GetString(JsonElement item, string propertyName)
        => item.GetProperty(propertyName).GetString() ?? string.Empty;
}
