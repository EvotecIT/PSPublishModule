using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Input used by <see cref="ProjectConsistencyWorkflowService"/> for project consistency analysis and conversion.
/// </summary>
public sealed class ProjectConsistencyWorkflowRequest
{
    /// <summary>Path to the project directory.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Logical project type used to resolve default file patterns.</summary>
    public string ProjectType { get; set; } = "Mixed";

    /// <summary>Custom file extensions used when <see cref="ProjectType"/> is Custom.</summary>
    public string[]? CustomExtensions { get; set; }

    /// <summary>Directory names to exclude from enumeration.</summary>
    public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" };

    /// <summary>File patterns to exclude from enumeration.</summary>
    public string[] ExcludeFiles { get; set; } = System.Array.Empty<string>();

    /// <summary>Recommended encoding for analysis mode.</summary>
    public TextEncodingKind? RecommendedEncoding { get; set; }

    /// <summary>Recommended line ending for analysis mode.</summary>
    public FileConsistencyLineEnding? RecommendedLineEnding { get; set; }

    /// <summary>Include detailed file-by-file analysis in the returned report.</summary>
    public bool IncludeDetails { get; set; }

    /// <summary>Optional CSV export path for the consistency report.</summary>
    public string? ExportPath { get; set; }

    /// <summary>Optional per-path encoding overrides.</summary>
    public IReadOnlyDictionary<string, FileConsistencyEncoding>? EncodingOverrides { get; set; }

    /// <summary>Optional per-path line ending overrides.</summary>
    public IReadOnlyDictionary<string, FileConsistencyLineEnding>? LineEndingOverrides { get; set; }

    /// <summary>Whether the encoding conversion switch was explicitly provided.</summary>
    public bool FixEncodingSpecified { get; set; }

    /// <summary>Whether encoding conversion should run.</summary>
    public bool FixEncoding { get; set; }

    /// <summary>Whether the line ending conversion switch was explicitly provided.</summary>
    public bool FixLineEndingsSpecified { get; set; }

    /// <summary>Whether line ending conversion should run.</summary>
    public bool FixLineEndings { get; set; }

    /// <summary>Source encoding filter for conversion mode.</summary>
    public TextEncodingKind SourceEncoding { get; set; } = TextEncodingKind.Any;

    /// <summary>Required encoding for conversion mode.</summary>
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;

    /// <summary>Required line ending for conversion mode.</summary>
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;

    /// <summary>Create backups before conversion.</summary>
    public bool CreateBackups { get; set; }

    /// <summary>Optional backup directory for conversion mode.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Force conversion even when files already match the target.</summary>
    public bool Force { get; set; }

    /// <summary>Do not rollback from backup on encoding verification mismatch.</summary>
    public bool NoRollbackOnMismatch { get; set; }

    /// <summary>Only convert files with mixed line endings.</summary>
    public bool OnlyMixedLineEndings { get; set; }

    /// <summary>Ensure a final newline exists after line ending conversion.</summary>
    public bool EnsureFinalNewline { get; set; }

    /// <summary>Only fix files missing the final newline.</summary>
    public bool OnlyMissingFinalNewline { get; set; }
}
