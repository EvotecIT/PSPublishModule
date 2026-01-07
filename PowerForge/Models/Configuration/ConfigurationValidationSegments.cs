namespace PowerForge;

/// <summary>
/// Configuration segment that describes PowerShell compatibility checking.
/// </summary>
public sealed class ConfigurationCompatibilitySegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Compatibility";

    /// <summary>Compatibility settings payload.</summary>
    public CompatibilitySettings Settings { get; set; } = new();
}

/// <summary>
/// Compatibility settings payload for <see cref="ConfigurationCompatibilitySegment"/>.
/// </summary>
public sealed class CompatibilitySettings
{
    /// <summary>Enable PowerShell compatibility checking during build.</summary>
    public bool Enable { get; set; }

    /// <summary>Fail the build if compatibility issues are found.</summary>
    public bool FailOnIncompatibility { get; set; }

    /// <summary>Require PowerShell 5.1 compatibility.</summary>
    public bool RequirePS51Compatibility { get; set; }

    /// <summary>Require PowerShell 7 compatibility.</summary>
    public bool RequirePS7Compatibility { get; set; }

    /// <summary>Require cross-version compatibility (both PS 5.1 and PS 7).</summary>
    public bool RequireCrossCompatibility { get; set; }

    /// <summary>Minimum percentage of files that must be cross-compatible.</summary>
    public int MinimumCompatibilityPercentage { get; set; } = 95;

    /// <summary>Directory names to exclude from compatibility analysis.</summary>
    public string[] ExcludeDirectories { get; set; } = Array.Empty<string>();

    /// <summary>Export detailed compatibility report to the artifacts directory.</summary>
    public bool ExportReport { get; set; }

    /// <summary>Custom filename for the compatibility report.</summary>
    public string ReportFileName { get; set; } = "PowerShellCompatibilityReport.csv";
}

/// <summary>
/// Configuration segment that describes file consistency checking (encoding and line endings).
/// </summary>
public sealed class ConfigurationFileConsistencySegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "FileConsistency";

    /// <summary>File consistency settings payload.</summary>
    public FileConsistencySettings Settings { get; set; } = new();
}

/// <summary>
/// File consistency settings payload for <see cref="ConfigurationFileConsistencySegment"/>.
/// </summary>
public sealed class FileConsistencySettings
{
    /// <summary>Enable file consistency checking during build.</summary>
    public bool Enable { get; set; }

    /// <summary>Fail the build if consistency issues are found.</summary>
    public bool FailOnInconsistency { get; set; }

    /// <summary>Required file encoding.</summary>
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;

    /// <summary>Required line ending style.</summary>
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;

    /// <summary>
    /// Optional scope for consistency checks (staging/project). When null, <see cref="UpdateProjectRoot"/> is used for backward compatibility.
    /// </summary>
    public FileConsistencyScope? Scope { get; set; }

    /// <summary>Automatically fix encoding and line ending issues during build.</summary>
    public bool AutoFix { get; set; }

    /// <summary>Create backup files before applying automatic fixes.</summary>
    public bool CreateBackups { get; set; }

    /// <summary>Maximum percentage of files that can have consistency issues.</summary>
    public int MaxInconsistencyPercentage { get; set; } = 5;

    /// <summary>Directory names to exclude from consistency analysis.</summary>
    public string[] ExcludeDirectories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Per-path encoding overrides. Keys are file patterns (e.g., "*.xml", "Docs/*.md", ".ps1") and values are encodings.
    /// </summary>
    public Dictionary<string, FileConsistencyEncoding>? EncodingOverrides { get; set; }

    /// <summary>
    /// When true, applies consistency checks (and optional AutoFix) to the project root as well as staging output.
    /// </summary>
    public bool UpdateProjectRoot { get; set; }

    /// <summary>Export detailed consistency report to the artifacts directory.</summary>
    public bool ExportReport { get; set; }

    /// <summary>Custom filename for the consistency report.</summary>
    public string ReportFileName { get; set; } = "FileConsistencyReport.csv";

    /// <summary>Check for files with mixed line endings.</summary>
    public bool CheckMixedLineEndings { get; set; }

    /// <summary>Check for files missing final newlines.</summary>
    public bool CheckMissingFinalNewline { get; set; }

    /// <summary>
    /// Resolves the effective scope for file consistency checks.
    /// </summary>
    public FileConsistencyScope ResolveScope()
        => Scope ?? (UpdateProjectRoot ? FileConsistencyScope.StagingAndProject : FileConsistencyScope.StagingOnly);
}
