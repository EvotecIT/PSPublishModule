using System.Collections.ObjectModel;

namespace PowerForge;

/// <summary>One reviewed immutable host-artifact identity accepted as semantic-oracle evidence.</summary>
public sealed class PowerShellCompilationSemanticHostArtifactPin
{
    private readonly PowerShellCompilationSemanticHostArtifact _hostArtifact;

    /// <summary>Creates one reviewed exact-host pin.</summary>
    public PowerShellCompilationSemanticHostArtifactPin(
        string profileId,
        string releaseIdentity,
        string releaseTag,
        string trackedTagPrefix,
        string upstreamCommit,
        string releaseAssetUri,
        string releaseAssetSha256,
        PowerShellCompilationSemanticHostArtifact hostArtifact,
        IEnumerable<string> reviewedCaseIds)
    {
        ProfileId = Require(profileId, nameof(profileId));
        ReleaseIdentity = Require(releaseIdentity, nameof(releaseIdentity));
        ReleaseTag = releaseTag?.Trim() ?? string.Empty;
        TrackedTagPrefix = trackedTagPrefix?.Trim() ?? string.Empty;
        UpstreamCommit = upstreamCommit?.Trim().ToLowerInvariant() ?? string.Empty;
        ReleaseAssetUri = releaseAssetUri?.Trim() ?? string.Empty;
        ReleaseAssetSha256 = NormalizeOptionalSha256(releaseAssetSha256, nameof(releaseAssetSha256));
        if ((ReleaseAssetUri.Length == 0) != (ReleaseAssetSha256.Length == 0))
            throw new ArgumentException("A release asset URI and SHA-256 must be supplied together.");
        if (ReleaseAssetUri.Length > 0 &&
            (!Uri.TryCreate(ReleaseAssetUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A release asset URI must be an absolute HTTPS URI.", nameof(releaseAssetUri));
        if ((ReleaseTag.Length == 0) != (TrackedTagPrefix.Length == 0))
            throw new ArgumentException("A release tag and tracked tag prefix must be supplied together.");
        _hostArtifact = Clone(PowerShellCompilationSemanticHostArtifactService.Normalize(
            hostArtifact ?? throw new ArgumentNullException(nameof(hostArtifact))));
        ReviewedCaseIds = new ReadOnlyCollection<string>((reviewedCaseIds ?? throw new ArgumentNullException(nameof(reviewedCaseIds)))
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>Semantic profile accepted by this pin.</summary>
    public string ProfileId { get; }

    /// <summary>Human-readable exact release or Windows component identity.</summary>
    public string ReleaseIdentity { get; }

    /// <summary>Exact upstream release tag, or empty for a Windows component without public source tags.</summary>
    public string ReleaseTag { get; }

    /// <summary>Stable upstream tag prefix monitored for newer patch releases.</summary>
    public string TrackedTagPrefix { get; }

    /// <summary>Peeled upstream release commit when public source exists.</summary>
    public string UpstreamCommit { get; }

    /// <summary>Exact official release asset URI when the host is distributed as a standalone archive.</summary>
    public string ReleaseAssetUri { get; }

    /// <summary>SHA-256 of the exact official release archive.</summary>
    public string ReleaseAssetSha256 { get; }

    /// <summary>Canonical exact-host identity hash used by replay and promotion.</summary>
    public string HostArtifactIdentitySha256 => _hostArtifact.IdentitySha256;

    /// <summary>Minimized cases executed when this host pin was reviewed.</summary>
    public IReadOnlyList<string> ReviewedCaseIds { get; }

    /// <summary>Returns a detached copy of the reviewed exact-host metadata.</summary>
    public PowerShellCompilationSemanticHostArtifact GetHostArtifact() => Clone(_hostArtifact);

    private static PowerShellCompilationSemanticHostArtifact Clone(PowerShellCompilationSemanticHostArtifact value)
        => new()
        {
            SchemaVersion = value.SchemaVersion,
            ExecutableName = value.ExecutableName,
            ExecutableSha256 = value.ExecutableSha256,
            ExecutableLength = value.ExecutableLength,
            ExecutableFileVersion = value.ExecutableFileVersion,
            ExecutableProductVersion = value.ExecutableProductVersion,
            HostVersion = value.HostVersion,
            BuildVersion = value.BuildVersion,
            GitCommitId = value.GitCommitId,
            PowerShellEdition = value.PowerShellEdition,
            OperatingSystem = value.OperatingSystem,
            OperatingSystemVersion = value.OperatingSystemVersion,
            Architecture = value.Architecture,
            Culture = value.Culture,
            UICulture = value.UICulture,
            FeatureSwitches = value.FeatureSwitches.ToArray(),
            IdentitySha256 = value.IdentitySha256
        };

    private static string NormalizeOptionalSha256(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0) return string.Empty;
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A 64-character hexadecimal SHA-256 value is required.", parameterName);
        return normalized;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}
