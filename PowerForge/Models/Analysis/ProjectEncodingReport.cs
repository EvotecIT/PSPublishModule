using System;

namespace PowerForge;

/// <summary>Per-file encoding detail.</summary>
public sealed class ProjectEncodingFileDetail
{
    /// <summary>Path relative to the analyzed project root.</summary>
    public string RelativePath { get; }
    /// <summary>Full file system path.</summary>
    public string FullPath { get; }
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Detected encoding kind, or null when detection failed.</summary>
    public TextEncodingKind? Encoding { get; }
    /// <summary>File size in bytes.</summary>
    public long Size { get; }
    /// <summary>Last write time (local time).</summary>
    public DateTime LastModified { get; }
    /// <summary>Directory containing the file.</summary>
    public string Directory { get; }
    /// <summary>Error message when detection failed.</summary>
    public string? Error { get; }

    /// <summary>Creates a new per-file encoding detail.</summary>
    public ProjectEncodingFileDetail(
        string relativePath,
        string fullPath,
        string extension,
        TextEncodingKind? encoding,
        long size,
        DateTime lastModified,
        string directory,
        string? error)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Extension = extension;
        Encoding = encoding;
        Size = size;
        LastModified = lastModified;
        Directory = directory;
        Error = error;
    }
}

/// <summary>Encoding distribution item.</summary>
public sealed class ProjectEncodingDistributionItem
{
    /// <summary>Encoding kind.</summary>
    public TextEncodingKind Encoding { get; }
    /// <summary>Number of files detected with this encoding.</summary>
    public int Count { get; }

    /// <summary>Creates a new distribution item.</summary>
    public ProjectEncodingDistributionItem(TextEncodingKind encoding, int count)
    {
        Encoding = encoding;
        Count = count;
    }
}

/// <summary>Encoding distribution per file extension.</summary>
public sealed class ProjectEncodingExtensionDistribution
{
    /// <summary>File extension (lowercased, including leading dot).</summary>
    public string Extension { get; }
    /// <summary>Distribution of encodings for this extension.</summary>
    public ProjectEncodingDistributionItem[] Encodings { get; }

    /// <summary>Creates a new per-extension distribution.</summary>
    public ProjectEncodingExtensionDistribution(string extension, ProjectEncodingDistributionItem[] encodings)
    {
        Extension = extension;
        Encodings = encodings ?? Array.Empty<ProjectEncodingDistributionItem>();
    }
}

/// <summary>Summary of a project encoding analysis.</summary>
public sealed class ProjectEncodingSummary
{
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
    /// <summary>Most common detected encoding kind, or null when no encodings were detected.</summary>
    public TextEncodingKind? MostCommonEncoding { get; }
    /// <summary>Unique encodings found in the project.</summary>
    public TextEncodingKind[] UniqueEncodings { get; }
    /// <summary>Extensions that contain more than one encoding kind.</summary>
    public string[] InconsistentExtensions { get; }
    /// <summary>Encoding distribution across the project.</summary>
    public ProjectEncodingDistributionItem[] Distribution { get; }
    /// <summary>Encoding distribution grouped by file extension.</summary>
    public ProjectEncodingExtensionDistribution[] ExtensionMap { get; }
    /// <summary>Local timestamp when analysis was produced.</summary>
    public DateTime AnalysisDate { get; }

    /// <summary>Creates a new encoding analysis summary.</summary>
    public ProjectEncodingSummary(
        string projectPath,
        string projectType,
        ProjectKind kind,
        int totalFiles,
        int errorFiles,
        TextEncodingKind? mostCommonEncoding,
        TextEncodingKind[] uniqueEncodings,
        string[] inconsistentExtensions,
        ProjectEncodingDistributionItem[] distribution,
        ProjectEncodingExtensionDistribution[] extensionMap,
        DateTime analysisDate)
    {
        ProjectPath = projectPath;
        ProjectType = projectType;
        Kind = kind;
        TotalFiles = totalFiles;
        ErrorFiles = errorFiles;
        MostCommonEncoding = mostCommonEncoding;
        UniqueEncodings = uniqueEncodings ?? Array.Empty<TextEncodingKind>();
        InconsistentExtensions = inconsistentExtensions ?? Array.Empty<string>();
        Distribution = distribution ?? Array.Empty<ProjectEncodingDistributionItem>();
        ExtensionMap = extensionMap ?? Array.Empty<ProjectEncodingExtensionDistribution>();
        AnalysisDate = analysisDate;
    }
}

/// <summary>Grouping by encoding.</summary>
public sealed class ProjectEncodingGroup
{
    /// <summary>Encoding kind for this group.</summary>
    public TextEncodingKind Encoding { get; }
    /// <summary>Files in this group.</summary>
    public ProjectEncodingFileDetail[] Files { get; }

    /// <summary>Creates a new encoding group.</summary>
    public ProjectEncodingGroup(TextEncodingKind encoding, ProjectEncodingFileDetail[] files)
    {
        Encoding = encoding;
        Files = files ?? Array.Empty<ProjectEncodingFileDetail>();
    }
}

/// <summary>Result of project encoding analysis.</summary>
public sealed class ProjectEncodingReport
{
    /// <summary>Summary for the analysis.</summary>
    public ProjectEncodingSummary Summary { get; }
    /// <summary>Optional file details (when requested).</summary>
    public ProjectEncodingFileDetail[]? Files { get; }
    /// <summary>Optional grouping output (when requested).</summary>
    public ProjectEncodingGroup[]? GroupedByEncoding { get; }
    /// <summary>CSV export path (when requested).</summary>
    public string? ExportPath { get; }

    /// <summary>Creates a new encoding analysis report.</summary>
    public ProjectEncodingReport(
        ProjectEncodingSummary summary,
        ProjectEncodingFileDetail[]? files,
        ProjectEncodingGroup[]? groupedByEncoding,
        string? exportPath)
    {
        Summary = summary;
        Files = files;
        GroupedByEncoding = groupedByEncoding;
        ExportPath = exportPath;
    }
}
