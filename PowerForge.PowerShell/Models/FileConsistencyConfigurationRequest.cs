using System.Collections;

namespace PowerForge;

internal sealed class FileConsistencyConfigurationRequest
{
    public bool Enable { get; set; }
    public bool FailOnInconsistency { get; set; }
    public ValidationSeverity? Severity { get; set; }
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;
    public ProjectKind? ProjectKind { get; set; }
    public bool ProjectKindSpecified { get; set; }
    public string[]? IncludePatterns { get; set; }
    public FileConsistencyScope Scope { get; set; } = FileConsistencyScope.StagingOnly;
    public bool ScopeSpecified { get; set; }
    public bool AutoFix { get; set; }
    public bool CreateBackups { get; set; }
    public int MaxInconsistencyPercentage { get; set; } = 5;
    public string[]? ExcludeDirectories { get; set; }
    public string[]? ExcludeFiles { get; set; }
    public Hashtable? EncodingOverrides { get; set; }
    public Hashtable? LineEndingOverrides { get; set; }
    public bool UpdateProjectRoot { get; set; }
    public bool ExportReport { get; set; }
    public string ReportFileName { get; set; } = "FileConsistencyReport.csv";
    public bool CheckMixedLineEndings { get; set; }
    public bool CheckMissingFinalNewline { get; set; }
}
