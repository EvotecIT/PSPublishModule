using System;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Supported issue types emitted by <see cref="PowerShellCompatibilityAnalyzer"/>.
/// </summary>
public enum PowerShellCompatibilityIssueType
{
    /// <summary>Generic error (file missing, failed to read, failed to analyze).</summary>
    Error,
    /// <summary>Feature available only in PowerShell 7+.</summary>
    PowerShell7Feature,
    /// <summary>Feature available only in Windows PowerShell 5.1 or deprecated in PowerShell 7.</summary>
    PowerShell51Feature,
    /// <summary>Platform-specific behavior or availability differences.</summary>
    PlatformSpecific,
    /// <summary>Use of .NET Framework-only assemblies.</summary>
    DotNetFramework,
    /// <summary>Potential class inheritance behavior differences.</summary>
    ClassInheritance,
    /// <summary>PowerShell workflow usage (Windows PowerShell only).</summary>
    Workflow,
    /// <summary>PowerShell ISE-only features.</summary>
    ISE,
    /// <summary>Encoding issue that may impact Windows PowerShell 5.1.</summary>
    Encoding
}

/// <summary>
/// Severity level for a <see cref="PowerShellCompatibilityIssue"/>.
/// </summary>
public enum PowerShellCompatibilitySeverity
{
    /// <summary>Low severity issue.</summary>
    Low,
    /// <summary>Medium severity issue.</summary>
    Medium,
    /// <summary>High severity issue.</summary>
    High
}

/// <summary>
/// Compatibility issue found during PowerShell file analysis.
/// </summary>
public sealed class PowerShellCompatibilityIssue
{
    /// <summary>Issue type.</summary>
    public PowerShellCompatibilityIssueType Type { get; }
    /// <summary>Human readable description.</summary>
    public string Description { get; }
    /// <summary>Suggested remediation.</summary>
    public string Recommendation { get; }
    /// <summary>Issue severity.</summary>
    public PowerShellCompatibilitySeverity Severity { get; }

    /// <summary>Creates a new compatibility issue.</summary>
    public PowerShellCompatibilityIssue(
        PowerShellCompatibilityIssueType type,
        string description,
        string recommendation,
        PowerShellCompatibilitySeverity severity)
    {
        Type = type;
        Description = description ?? string.Empty;
        Recommendation = recommendation ?? string.Empty;
        Severity = severity;
    }
}

/// <summary>
/// Per-file compatibility analysis result.
/// </summary>
public sealed class PowerShellCompatibilityFileResult
{
    /// <summary>Full file system path.</summary>
    public string FullPath { get; }
    /// <summary>Path relative to the analyzed base directory (when possible).</summary>
    public string RelativePath { get; }
    /// <summary>Indicates whether the file is compatible with Windows PowerShell 5.1.</summary>
    public bool PowerShell51Compatible { get; }
    /// <summary>Indicates whether the file is compatible with PowerShell 7+.</summary>
    public bool PowerShell7Compatible { get; }
    /// <summary>Detected encoding kind, or null when unavailable.</summary>
    public TextEncodingKind? Encoding { get; }
    /// <summary>Issues discovered in this file.</summary>
    public PowerShellCompatibilityIssue[] Issues { get; }

    /// <summary>Creates a new per-file compatibility result.</summary>
    public PowerShellCompatibilityFileResult(
        string fullPath,
        string relativePath,
        bool powerShell51Compatible,
        bool powerShell7Compatible,
        TextEncodingKind? encoding,
        PowerShellCompatibilityIssue[] issues)
    {
        FullPath = fullPath ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        PowerShell51Compatible = powerShell51Compatible;
        PowerShell7Compatible = powerShell7Compatible;
        Encoding = encoding;
        Issues = issues ?? Array.Empty<PowerShellCompatibilityIssue>();
    }
}

/// <summary>
/// Aggregate summary produced by <see cref="PowerShellCompatibilityAnalyzer"/>.
/// </summary>
public sealed class PowerShellCompatibilitySummary
{
    /// <summary>Overall status for the analysis.</summary>
    public CheckStatus Status { get; }
    /// <summary>Local timestamp when analysis was produced.</summary>
    public DateTime AnalysisDate { get; }
    /// <summary>Total number of files analyzed.</summary>
    public int TotalFiles { get; }
    /// <summary>Number of files compatible with Windows PowerShell 5.1.</summary>
    public int PowerShell51Compatible { get; }
    /// <summary>Number of files compatible with PowerShell 7+.</summary>
    public int PowerShell7Compatible { get; }
    /// <summary>Number of files compatible with both Windows PowerShell 5.1 and PowerShell 7+.</summary>
    public int CrossCompatible { get; }
    /// <summary>Number of files that have one or more issues.</summary>
    public int FilesWithIssues { get; }
    /// <summary>Percentage of files that are cross-compatible.</summary>
    public double CrossCompatibilityPercentage { get; }
    /// <summary>Human readable summary message.</summary>
    public string Message { get; }
    /// <summary>Recommended follow-up actions.</summary>
    public string[] Recommendations { get; }

    /// <summary>Creates a new compatibility summary.</summary>
    public PowerShellCompatibilitySummary(
        CheckStatus status,
        DateTime analysisDate,
        int totalFiles,
        int powerShell51Compatible,
        int powerShell7Compatible,
        int crossCompatible,
        int filesWithIssues,
        double crossCompatibilityPercentage,
        string message,
        string[] recommendations)
    {
        Status = status;
        AnalysisDate = analysisDate;
        TotalFiles = totalFiles;
        PowerShell51Compatible = powerShell51Compatible;
        PowerShell7Compatible = powerShell7Compatible;
        CrossCompatible = crossCompatible;
        FilesWithIssues = filesWithIssues;
        CrossCompatibilityPercentage = crossCompatibilityPercentage;
        Message = message ?? string.Empty;
        Recommendations = recommendations ?? Array.Empty<string>();
    }
}

/// <summary>
/// Report produced by <see cref="PowerShellCompatibilityAnalyzer"/>.
/// </summary>
public sealed class PowerShellCompatibilityReport
{
    /// <summary>Summary for the analysis.</summary>
    public PowerShellCompatibilitySummary Summary { get; }
    /// <summary>Per-file results.</summary>
    public PowerShellCompatibilityFileResult[] Files { get; }
    /// <summary>CSV export path (when requested).</summary>
    public string? ExportPath { get; }

    /// <summary>Creates a new compatibility report.</summary>
    public PowerShellCompatibilityReport(
        PowerShellCompatibilitySummary summary,
        PowerShellCompatibilityFileResult[] files,
        string? exportPath)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Files = files ?? Array.Empty<PowerShellCompatibilityFileResult>();
        ExportPath = exportPath;
    }
}

/// <summary>
/// Input specification for <see cref="PowerShellCompatibilityAnalyzer"/>.
/// </summary>
public sealed class PowerShellCompatibilitySpec
{
    /// <summary>Path to a file or directory to analyze.</summary>
    public string Path { get; }
    /// <summary>When analyzing a directory, recursively analyze all subdirectories.</summary>
    public bool Recurse { get; }
    /// <summary>Directory name substrings to exclude from analysis.</summary>
    public string[] ExcludeDirectories { get; }

    /// <summary>Creates a new spec for compatibility analysis.</summary>
    public PowerShellCompatibilitySpec(string path, bool recurse, string[]? excludeDirectories)
    {
        Path = System.IO.Path.GetFullPath((path ?? string.Empty).Trim().Trim('"'));
        Recurse = recurse;
        ExcludeDirectories = (excludeDirectories ?? new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore" })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
}

/// <summary>
/// Progress information emitted by <see cref="PowerShellCompatibilityAnalyzer"/>.
/// </summary>
public sealed class PowerShellCompatibilityProgress
{
    /// <summary>1-based index of the current file.</summary>
    public int Current { get; }
    /// <summary>Total number of files to analyze.</summary>
    public int Total { get; }
    /// <summary>Current file being processed.</summary>
    public string FilePath { get; }

    /// <summary>Creates a new progress event.</summary>
    public PowerShellCompatibilityProgress(int current, int total, string filePath)
    {
        Current = current;
        Total = total;
        FilePath = filePath ?? string.Empty;
    }
}
