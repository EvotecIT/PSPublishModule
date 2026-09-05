using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Exact executable and runtime identity used for one semantic-oracle observation.</summary>
public sealed class PowerShellCompilationSemanticHostArtifact
{
    /// <summary>Host-artifact schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Portable executable file name.</summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact executable bytes.</summary>
    public string ExecutableSha256 { get; set; } = string.Empty;

    /// <summary>Executable byte length.</summary>
    public long ExecutableLength { get; set; }

    /// <summary>Executable file-version identity.</summary>
    public string ExecutableFileVersion { get; set; } = string.Empty;

    /// <summary>Executable product-version identity.</summary>
    public string ExecutableProductVersion { get; set; } = string.Empty;

    /// <summary>Exact PowerShell runtime version.</summary>
    public string HostVersion { get; set; } = string.Empty;

    /// <summary>Windows build identity when supplied by the host.</summary>
    public string BuildVersion { get; set; } = string.Empty;

    /// <summary>PowerShell release/source identity reported by the host.</summary>
    public string GitCommitId { get; set; } = string.Empty;

    /// <summary>PowerShell edition.</summary>
    public string PowerShellEdition { get; set; } = string.Empty;

    /// <summary>Normalized operating-system family.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Exact operating-system version description reported by the host.</summary>
    public string OperatingSystemVersion { get; set; } = string.Empty;

    /// <summary>Process architecture.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Current culture used by the observation.</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>Current UI culture used by the observation.</summary>
    public string UICulture { get; set; } = string.Empty;

    /// <summary>Sorted semantic feature switches owned by the selected profile.</summary>
    public string[] FeatureSwitches { get; set; } = Array.Empty<string>();

    /// <summary>Canonical SHA-256 over every exact host-artifact field.</summary>
    public string IdentitySha256 { get; set; } = string.Empty;
}

/// <summary>Canonical normalization and hashing for semantic host artifacts.</summary>
public static class PowerShellCompilationSemanticHostArtifactService
{
    /// <summary>Normalizes, validates, and integrity-binds one observed host artifact.</summary>
    public static PowerShellCompilationSemanticHostArtifact Normalize(PowerShellCompilationSemanticHostArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (artifact.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported semantic host-artifact schema {artifact.SchemaVersion}.");
        artifact.ExecutableName = Require(artifact.ExecutableName, nameof(artifact.ExecutableName));
        artifact.ExecutableSha256 = NormalizeSha256(artifact.ExecutableSha256, nameof(artifact.ExecutableSha256));
        if (artifact.ExecutableLength <= 0)
            throw new InvalidOperationException("Semantic host executable length must be positive.");
        artifact.ExecutableFileVersion = artifact.ExecutableFileVersion?.Trim() ?? string.Empty;
        artifact.ExecutableProductVersion = artifact.ExecutableProductVersion?.Trim() ?? string.Empty;
        artifact.HostVersion = Require(artifact.HostVersion, nameof(artifact.HostVersion));
        artifact.BuildVersion = artifact.BuildVersion?.Trim() ?? string.Empty;
        artifact.GitCommitId = artifact.GitCommitId?.Trim() ?? string.Empty;
        artifact.PowerShellEdition = Require(artifact.PowerShellEdition, nameof(artifact.PowerShellEdition));
        artifact.OperatingSystem = Require(artifact.OperatingSystem, nameof(artifact.OperatingSystem));
        artifact.OperatingSystemVersion = Require(artifact.OperatingSystemVersion, nameof(artifact.OperatingSystemVersion));
        artifact.Architecture = Require(artifact.Architecture, nameof(artifact.Architecture));
        artifact.Culture = Require(artifact.Culture, nameof(artifact.Culture));
        artifact.UICulture = Require(artifact.UICulture, nameof(artifact.UICulture));
        artifact.FeatureSwitches = (artifact.FeatureSwitches ?? Array.Empty<string>())
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        var suppliedIdentity = artifact.IdentitySha256?.Trim() ?? string.Empty;
        artifact.IdentitySha256 = string.Empty;
        var actualIdentity = ComputeSha256(artifact);
        if (suppliedIdentity.Length > 0 &&
            !string.Equals(NormalizeSha256(suppliedIdentity, nameof(artifact.IdentitySha256)), actualIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("Semantic host-artifact identity does not match its exact executable/runtime fields.");
        artifact.IdentitySha256 = actualIdentity;
        return artifact;
    }

    /// <summary>Computes the canonical SHA-256 over one normalized host artifact.</summary>
    public static string ComputeSha256(PowerShellCompilationSemanticHostArtifact artifact)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        var builder = new StringBuilder();
        AppendCanonical(builder, "SchemaVersion", artifact.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendCanonical(builder, "ExecutableName", artifact.ExecutableName);
        AppendCanonical(builder, "ExecutableSha256", artifact.ExecutableSha256);
        AppendCanonical(builder, "ExecutableLength", artifact.ExecutableLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendCanonical(builder, "ExecutableFileVersion", artifact.ExecutableFileVersion);
        AppendCanonical(builder, "ExecutableProductVersion", artifact.ExecutableProductVersion);
        AppendCanonical(builder, "HostVersion", artifact.HostVersion);
        AppendCanonical(builder, "BuildVersion", artifact.BuildVersion);
        AppendCanonical(builder, "GitCommitId", artifact.GitCommitId);
        AppendCanonical(builder, "PowerShellEdition", artifact.PowerShellEdition);
        AppendCanonical(builder, "OperatingSystem", artifact.OperatingSystem);
        AppendCanonical(builder, "OperatingSystemVersion", artifact.OperatingSystemVersion);
        AppendCanonical(builder, "Architecture", artifact.Architecture);
        AppendCanonical(builder, "Culture", artifact.Culture);
        AppendCanonical(builder, "UICulture", artifact.UICulture);
        var featureSwitches = artifact.FeatureSwitches ?? Array.Empty<string>();
        AppendCanonical(builder, "FeatureSwitchCount", featureSwitches.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 0; index < featureSwitches.Length; index++)
            AppendCanonical(builder, "FeatureSwitch[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", featureSwitches[index]);
        using var algorithm = SHA256.Create();
        var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        builder.Clear();
        builder.EnsureCapacity(bytes.Length * 2);
        foreach (var value in bytes)
            builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>Requires one normalized host artifact to satisfy its selected semantic profile.</summary>
    public static void EnsureMatchesProfile(
        PowerShellCompilationSemanticHostArtifact artifact,
        PowerShellCompilationSemanticOracleProfile profile,
        string? expectedCulture = null)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        artifact = Normalize(artifact);
        if (!string.Equals(profile.PowerShellEdition, artifact.PowerShellEdition, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Semantic profile '{profile.ProfileId}' requires PowerShell edition '{profile.PowerShellEdition}', but host reported '{artifact.PowerShellEdition}'.");
        if (!Version.TryParse(artifact.HostVersion, out var version))
            throw new InvalidOperationException($"Semantic oracle reported invalid host version '{artifact.HostVersion}'.");
        var expectedMajor = profile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51 ? 5 : 7;
        var expectedMinor = profile.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId ? 4
            : profile.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId ? 6
            : 1;
        if (version.Major != expectedMajor || version.Minor != expectedMinor)
            throw new InvalidOperationException($"Semantic profile '{profile.ProfileId}' does not accept host version '{version}'.");
        if (profile.OperatingSystem != "Any" && !string.Equals(profile.OperatingSystem, artifact.OperatingSystem, StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException($"Semantic profile '{profile.ProfileId}' requires {profile.OperatingSystem}, but host reported {artifact.OperatingSystem}.");
        if (profile.Architecture != "Any" && !string.Equals(profile.Architecture, artifact.Architecture, StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException($"Semantic profile '{profile.ProfileId}' requires {profile.Architecture}, but host reported {artifact.Architecture}.");
        if (!artifact.FeatureSwitches.SequenceEqual(profile.FeatureSwitches, StringComparer.Ordinal))
            throw new InvalidOperationException("Semantic oracle returned feature switches that do not match the selected profile.");
        if (!string.IsNullOrWhiteSpace(expectedCulture) &&
            (!string.Equals(expectedCulture, artifact.Culture, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(expectedCulture, artifact.UICulture, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Semantic oracle did not apply requested culture '{expectedCulture}'.");
    }

    private static void AppendCanonical(StringBuilder builder, string name, string? value)
    {
        value ??= string.Empty;
        builder.Append(name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(':').Append(name)
            .Append('=')
            .Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(':').Append(value)
            .Append(';');
    }

    private static string NormalizeSha256(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A 64-character hexadecimal SHA-256 value is required.", parameterName);
        return normalized;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}

/// <summary>One structured record written to a non-error PowerShell stream.</summary>
public sealed class PowerShellCompilationSemanticStreamObservation
{
    /// <summary>Cross-stream sequence assigned at the observation boundary.</summary>
    public int Sequence { get; set; }

    /// <summary>Stream name: Information, Warning, Verbose, or Debug.</summary>
    public string Stream { get; set; } = string.Empty;

    /// <summary>Normalized record message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Exact runtime record type.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Information tags, preserving their emitted order.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>One structured PowerShell error observation.</summary>
public sealed class PowerShellCompilationSemanticErrorObservation
{
    /// <summary>Cross-stream sequence assigned at the observation boundary.</summary>
    public int Sequence { get; set; }

    /// <summary>Normalized error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Fully-qualified PowerShell error identity.</summary>
    public string FullyQualifiedErrorId { get; set; } = string.Empty;

    /// <summary>PowerShell error category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Underlying exception type.</summary>
    public string ExceptionTypeName { get; set; } = string.Empty;

    /// <summary>Target-object runtime type, or empty when no target exists.</summary>
    public string TargetTypeName { get; set; } = string.Empty;

    /// <summary>Whether the error terminated the observed pipeline.</summary>
    public bool IsTerminating { get; set; }
}
