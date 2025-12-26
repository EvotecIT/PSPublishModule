using System;

namespace PowerForge;

/// <summary>Per-file line ending detail.</summary>
public sealed class ProjectLineEndingFileDetail
{
    /// <summary>Path relative to the analyzed project root.</summary>
    public string RelativePath { get; }
    /// <summary>Full file system path.</summary>
    public string FullPath { get; }
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Detected line ending kind.</summary>
    public DetectedLineEndingKind LineEnding { get; }
    /// <summary>Whether the file ends with a final newline.</summary>
    public bool HasFinalNewline { get; }
    /// <summary>File size in bytes.</summary>
    public long Size { get; }
    /// <summary>Last write time (local time).</summary>
    public DateTime LastModified { get; }
    /// <summary>Directory containing the file.</summary>
    public string Directory { get; }
    /// <summary>Error message when detection failed.</summary>
    public string? Error { get; }

    /// <summary>Creates a new per-file line ending detail.</summary>
    public ProjectLineEndingFileDetail(
        string relativePath,
        string fullPath,
        string extension,
        DetectedLineEndingKind lineEnding,
        bool hasFinalNewline,
        long size,
        DateTime lastModified,
        string directory,
        string? error)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Extension = extension;
        LineEnding = lineEnding;
        HasFinalNewline = hasFinalNewline;
        Size = size;
        LastModified = lastModified;
        Directory = directory;
        Error = error;
    }
}

/// <summary>Line ending distribution item.</summary>
public sealed class ProjectLineEndingDistributionItem
{
    /// <summary>Line ending kind.</summary>
    public DetectedLineEndingKind LineEnding { get; }
    /// <summary>Number of files detected with this line ending kind.</summary>
    public int Count { get; }

    /// <summary>Creates a new distribution item.</summary>
    public ProjectLineEndingDistributionItem(DetectedLineEndingKind lineEnding, int count)
    {
        LineEnding = lineEnding;
        Count = count;
    }
}

/// <summary>Line ending distribution per file extension.</summary>
public sealed class ProjectLineEndingExtensionDistribution
{
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Distribution of line endings for this extension.</summary>
    public ProjectLineEndingDistributionItem[] LineEndings { get; }

    /// <summary>Creates a new per-extension distribution.</summary>
    public ProjectLineEndingExtensionDistribution(string extension, ProjectLineEndingDistributionItem[] lineEndings)
    {
        Extension = extension;
        LineEndings = lineEndings ?? Array.Empty<ProjectLineEndingDistributionItem>();
    }
}

/// <summary>Summary of a project line ending analysis.</summary>
public sealed class ProjectLineEndingSummary
{
    /// <summary>Overall check status.</summary>
    public CheckStatus Status { get; }
    /// <summary>Analyzed project root path.</summary>
    public string ProjectPath { get; }
    /// <summary>User-selected project type (PowerShell/CSharp/Mixed/All/Custom).</summary>
    public string ProjectType { get; }
    /// <summary>Resolved project kind used for enumeration.</summary>
    public ProjectKind Kind { get; }
    /// <summary>Total number of files considered.</summary>
    public int TotalFiles { get; }
    /// <summary>Number of files that produced errors during analysis.</summary>
    public int ErrorFiles { get; }
    /// <summary>Most common detected line ending kind.</summary>
    public DetectedLineEndingKind MostCommonLineEnding { get; }
    /// <summary>Unique line ending kinds found in the project.</summary>
    public DetectedLineEndingKind[] UniqueLineEndings { get; }
    /// <summary>Extensions that contain more than one line ending kind.</summary>
    public string[] InconsistentExtensions { get; }
    /// <summary>Number of files with mixed line endings.</summary>
    public int ProblemFiles { get; }
    /// <summary>Number of files missing the final newline (for typical text extensions).</summary>
    public int FilesMissingFinalNewline { get; }
    /// <summary>Short human-readable summary message.</summary>
    public string Message { get; }
    /// <summary>Recommended actions to address problems.</summary>
    public string[] Recommendations { get; }
    /// <summary>Line ending distribution across the project.</summary>
    public ProjectLineEndingDistributionItem[] Distribution { get; }
    /// <summary>Line ending distribution grouped by file extension.</summary>
    public ProjectLineEndingExtensionDistribution[] ExtensionMap { get; }
    /// <summary>Local timestamp when analysis was produced.</summary>
    public DateTime AnalysisDate { get; }

    /// <summary>Creates a new line ending analysis summary.</summary>
    public ProjectLineEndingSummary(
        CheckStatus status,
        string projectPath,
        string projectType,
        ProjectKind kind,
        int totalFiles,
        int errorFiles,
        DetectedLineEndingKind mostCommonLineEnding,
        DetectedLineEndingKind[] uniqueLineEndings,
        string[] inconsistentExtensions,
        int problemFiles,
        int filesMissingFinalNewline,
        string message,
        string[] recommendations,
        ProjectLineEndingDistributionItem[] distribution,
        ProjectLineEndingExtensionDistribution[] extensionMap,
        DateTime analysisDate)
    {
        Status = status;
        ProjectPath = projectPath;
        ProjectType = projectType;
        Kind = kind;
        TotalFiles = totalFiles;
        ErrorFiles = errorFiles;
        MostCommonLineEnding = mostCommonLineEnding;
        UniqueLineEndings = uniqueLineEndings ?? Array.Empty<DetectedLineEndingKind>();
        InconsistentExtensions = inconsistentExtensions ?? Array.Empty<string>();
        ProblemFiles = problemFiles;
        FilesMissingFinalNewline = filesMissingFinalNewline;
        Message = message;
        Recommendations = recommendations ?? Array.Empty<string>();
        Distribution = distribution ?? Array.Empty<ProjectLineEndingDistributionItem>();
        ExtensionMap = extensionMap ?? Array.Empty<ProjectLineEndingExtensionDistribution>();
        AnalysisDate = analysisDate;
    }
}

/// <summary>Grouping by line ending.</summary>
public sealed class ProjectLineEndingGroup
{
    /// <summary>Line ending kind for this group.</summary>
    public DetectedLineEndingKind LineEnding { get; }
    /// <summary>Files in this group.</summary>
    public ProjectLineEndingFileDetail[] Files { get; }

    /// <summary>Creates a new line ending group.</summary>
    public ProjectLineEndingGroup(DetectedLineEndingKind lineEnding, ProjectLineEndingFileDetail[] files)
    {
        LineEnding = lineEnding;
        Files = files ?? Array.Empty<ProjectLineEndingFileDetail>();
    }
}

/// <summary>Result of project line ending analysis.</summary>
public sealed class ProjectLineEndingReport
{
    /// <summary>Summary for the analysis.</summary>
    public ProjectLineEndingSummary Summary { get; }
    /// <summary>Optional file details (when requested).</summary>
    public ProjectLineEndingFileDetail[]? Files { get; }
    /// <summary>Files detected with mixed line endings.</summary>
    public ProjectLineEndingFileDetail[] ProblemFiles { get; }
    /// <summary>Optional grouping output (when requested).</summary>
    public ProjectLineEndingGroup[]? GroupedByLineEnding { get; }
    /// <summary>CSV export path (when requested).</summary>
    public string? ExportPath { get; }

    /// <summary>Creates a new line ending analysis report.</summary>
    public ProjectLineEndingReport(
        ProjectLineEndingSummary summary,
        ProjectLineEndingFileDetail[]? files,
        ProjectLineEndingFileDetail[] problemFiles,
        ProjectLineEndingGroup[]? groupedByLineEnding,
        string? exportPath)
    {
        Summary = summary;
        Files = files;
        ProblemFiles = problemFiles ?? Array.Empty<ProjectLineEndingFileDetail>();
        GroupedByLineEnding = groupedByLineEnding;
        ExportPath = exportPath;
    }
}
