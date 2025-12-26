using System;

namespace PowerForge;

/// <summary>Per-extension issue summary for consistency analysis.</summary>
public sealed class ProjectConsistencyExtensionIssue
{
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Total number of files with issues for this extension.</summary>
    public int Total { get; }
    /// <summary>Number of files with encoding issues for this extension.</summary>
    public int EncodingIssues { get; }
    /// <summary>Number of files with line ending issues for this extension.</summary>
    public int LineEndingIssues { get; }
    /// <summary>Number of files with mixed line endings for this extension.</summary>
    public int MixedLineEndings { get; }
    /// <summary>Number of files missing final newline for this extension.</summary>
    public int MissingFinalNewline { get; }

    /// <summary>Creates a new per-extension issue summary.</summary>
    public ProjectConsistencyExtensionIssue(
        string extension,
        int total,
        int encodingIssues,
        int lineEndingIssues,
        int mixedLineEndings,
        int missingFinalNewline)
    {
        Extension = extension;
        Total = total;
        EncodingIssues = encodingIssues;
        LineEndingIssues = lineEndingIssues;
        MixedLineEndings = mixedLineEndings;
        MissingFinalNewline = missingFinalNewline;
    }
}

/// <summary>Per-file consistency detail.</summary>
public sealed class ProjectConsistencyFileDetail
{
    /// <summary>Path relative to the analyzed project root.</summary>
    public string RelativePath { get; }
    /// <summary>Full file system path.</summary>
    public string FullPath { get; }
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Detected encoding kind, or null when detection failed.</summary>
    public TextEncodingKind? CurrentEncoding { get; }
    /// <summary>Detected line ending kind.</summary>
    public DetectedLineEndingKind CurrentLineEnding { get; }
    /// <summary>Whether the file ends with a final newline.</summary>
    public bool HasFinalNewline { get; }
    /// <summary>Recommended/target encoding kind.</summary>
    public TextEncodingKind RecommendedEncoding { get; }
    /// <summary>Recommended/target line ending kind.</summary>
    public FileConsistencyLineEnding RecommendedLineEnding { get; }
    /// <summary>True when encoding differs from the recommended encoding.</summary>
    public bool NeedsEncodingConversion { get; }
    /// <summary>True when line endings differ from the recommended line ending.</summary>
    public bool NeedsLineEndingConversion { get; }
    /// <summary>True when mixed line endings were detected.</summary>
    public bool HasMixedLineEndings { get; }
    /// <summary>True when the file is missing the final newline.</summary>
    public bool MissingFinalNewline { get; }
    /// <summary>True when any of the issue flags are set.</summary>
    public bool HasIssues { get; }
    /// <summary>File size in bytes.</summary>
    public long Size { get; }
    /// <summary>Last write time (local time).</summary>
    public DateTime LastModified { get; }
    /// <summary>Directory containing the file.</summary>
    public string Directory { get; }
    /// <summary>Error message when analysis failed.</summary>
    public string? Error { get; }

    /// <summary>Creates a new per-file consistency detail.</summary>
    public ProjectConsistencyFileDetail(
        string relativePath,
        string fullPath,
        string extension,
        TextEncodingKind? currentEncoding,
        DetectedLineEndingKind currentLineEnding,
        bool hasFinalNewline,
        TextEncodingKind recommendedEncoding,
        FileConsistencyLineEnding recommendedLineEnding,
        bool needsEncodingConversion,
        bool needsLineEndingConversion,
        bool hasMixedLineEndings,
        bool missingFinalNewline,
        long size,
        DateTime lastModified,
        string directory,
        string? error)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Extension = extension;
        CurrentEncoding = currentEncoding;
        CurrentLineEnding = currentLineEnding;
        HasFinalNewline = hasFinalNewline;
        RecommendedEncoding = recommendedEncoding;
        RecommendedLineEnding = recommendedLineEnding;
        NeedsEncodingConversion = needsEncodingConversion;
        NeedsLineEndingConversion = needsLineEndingConversion;
        HasMixedLineEndings = hasMixedLineEndings;
        MissingFinalNewline = missingFinalNewline;
        HasIssues = needsEncodingConversion || needsLineEndingConversion || hasMixedLineEndings || missingFinalNewline;
        Size = size;
        LastModified = lastModified;
        Directory = directory;
        Error = error;
    }
}

/// <summary>Summary of a project consistency analysis.</summary>
public sealed class ProjectConsistencySummary
{
    /// <summary>Analyzed project root path.</summary>
    public string ProjectPath { get; }
    /// <summary>User-selected project type (PowerShell/CSharp/Mixed/All/Custom).</summary>
    public string ProjectType { get; }
    /// <summary>Resolved project kind used for enumeration.</summary>
    public ProjectKind Kind { get; }
    /// <summary>Local timestamp when analysis was produced.</summary>
    public DateTime AnalysisDate { get; }
    /// <summary>Total number of files considered.</summary>
    public int TotalFiles { get; }
    /// <summary>Number of files that have no detected issues.</summary>
    public int FilesCompliant { get; }
    /// <summary>Number of files that have at least one issue.</summary>
    public int FilesWithIssues { get; }
    /// <summary>Compliance percentage (0–100).</summary>
    public double CompliancePercentage { get; }
    /// <summary>Encoding distribution across the project.</summary>
    public ProjectEncodingDistributionItem[] CurrentEncodingDistribution { get; }
    /// <summary>Count of files needing encoding conversion.</summary>
    public int FilesNeedingEncodingConversion { get; }
    /// <summary>Recommended/target encoding kind.</summary>
    public TextEncodingKind RecommendedEncoding { get; }
    /// <summary>Line ending distribution across the project.</summary>
    public ProjectLineEndingDistributionItem[] CurrentLineEndingDistribution { get; }
    /// <summary>Count of files needing line ending conversion.</summary>
    public int FilesNeedingLineEndingConversion { get; }
    /// <summary>Count of files with mixed line endings.</summary>
    public int FilesWithMixedLineEndings { get; }
    /// <summary>Count of files missing the final newline.</summary>
    public int FilesMissingFinalNewline { get; }
    /// <summary>Recommended/target line ending kind.</summary>
    public FileConsistencyLineEnding RecommendedLineEnding { get; }
    /// <summary>Per-extension issue summary.</summary>
    public ProjectConsistencyExtensionIssue[] ExtensionIssues { get; }

    /// <summary>Creates a new project consistency summary.</summary>
    public ProjectConsistencySummary(
        string projectPath,
        string projectType,
        ProjectKind kind,
        DateTime analysisDate,
        int totalFiles,
        int filesCompliant,
        int filesWithIssues,
        double compliancePercentage,
        ProjectEncodingDistributionItem[] currentEncodingDistribution,
        int filesNeedingEncodingConversion,
        TextEncodingKind recommendedEncoding,
        ProjectLineEndingDistributionItem[] currentLineEndingDistribution,
        int filesNeedingLineEndingConversion,
        int filesWithMixedLineEndings,
        int filesMissingFinalNewline,
        FileConsistencyLineEnding recommendedLineEnding,
        ProjectConsistencyExtensionIssue[] extensionIssues)
    {
        ProjectPath = projectPath;
        ProjectType = projectType;
        Kind = kind;
        AnalysisDate = analysisDate;
        TotalFiles = totalFiles;
        FilesCompliant = filesCompliant;
        FilesWithIssues = filesWithIssues;
        CompliancePercentage = compliancePercentage;
        CurrentEncodingDistribution = currentEncodingDistribution ?? Array.Empty<ProjectEncodingDistributionItem>();
        FilesNeedingEncodingConversion = filesNeedingEncodingConversion;
        RecommendedEncoding = recommendedEncoding;
        CurrentLineEndingDistribution = currentLineEndingDistribution ?? Array.Empty<ProjectLineEndingDistributionItem>();
        FilesNeedingLineEndingConversion = filesNeedingLineEndingConversion;
        FilesWithMixedLineEndings = filesWithMixedLineEndings;
        FilesMissingFinalNewline = filesMissingFinalNewline;
        RecommendedLineEnding = recommendedLineEnding;
        ExtensionIssues = extensionIssues ?? Array.Empty<ProjectConsistencyExtensionIssue>();
    }
}

/// <summary>Result of project consistency analysis.</summary>
public sealed class ProjectConsistencyReport
{
    /// <summary>Summary for the analysis.</summary>
    public ProjectConsistencySummary Summary { get; }
    /// <summary>Encoding analysis results for the same project scan.</summary>
    public ProjectEncodingReport EncodingReport { get; }
    /// <summary>Line ending analysis results for the same project scan.</summary>
    public ProjectLineEndingReport LineEndingReport { get; }
    /// <summary>Optional per-file details (when requested).</summary>
    public ProjectConsistencyFileDetail[]? Files { get; }
    /// <summary>Files with at least one issue.</summary>
    public ProjectConsistencyFileDetail[] ProblematicFiles { get; }
    /// <summary>CSV export path (when requested).</summary>
    public string? ExportPath { get; }

    /// <summary>Creates a new project consistency report.</summary>
    public ProjectConsistencyReport(
        ProjectConsistencySummary summary,
        ProjectEncodingReport encodingReport,
        ProjectLineEndingReport lineEndingReport,
        ProjectConsistencyFileDetail[]? files,
        ProjectConsistencyFileDetail[] problematicFiles,
        string? exportPath)
    {
        Summary = summary;
        EncodingReport = encodingReport;
        LineEndingReport = lineEndingReport;
        Files = files;
        ProblematicFiles = problematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
        ExportPath = exportPath;
    }
}
