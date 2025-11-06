namespace PowerForge;

/// <summary>Project type used to determine default include patterns.</summary>
public enum ProjectKind
{
    /// <summary>PowerShell sources (ps1, psm1, psd1, ps1xml).</summary>
    PowerShell,
    /// <summary>C# sources and common project/config files.</summary>
    CSharp,
    /// <summary>Common mix of PowerShell and C# sources.</summary>
    Mixed,
    /// <summary>Common source extensions across multiple ecosystems.</summary>
    All
}

/// <summary>Logical text encodings supported by the encoding converter.</summary>
public enum TextEncodingKind
{
    /// <summary>ASCII (code page 20127).</summary>
    Ascii,
    /// <summary>UTF-16 Big Endian.</summary>
    BigEndianUnicode,
    /// <summary>UTF-16 Little Endian.</summary>
    Unicode,
    /// <summary>UTF-8 without BOM.</summary>
    UTF8,
    /// <summary>UTF-8 with BOM.</summary>
    UTF8BOM,
    /// <summary>UTF-32 Little Endian.</summary>
    UTF32,
    /// <summary>System default (not recommended for source control).</summary>
    Default,
    /// <summary>OEM code page (legacy).</summary>
    OEM,
    /// <summary>Accept any detected source encoding.</summary>
    Any
}

/// <summary>Options for project-wide file enumeration.</summary>
public sealed class ProjectEnumeration
{
    /// <summary>Root path to scan.</summary>
    public string RootPath { get; }
    /// <summary>Project kind to derive default patterns from.</summary>
    public ProjectKind Kind { get; }
    /// <summary>Optional custom patterns (e.g., *.ps1,*.psm1) when overriding kind.</summary>
    public IReadOnlyList<string>? CustomExtensions { get; }
    /// <summary>Directory names to exclude (exact name match).</summary>
    public IReadOnlyList<string> ExcludeDirectories { get; }

    /// <summary>
    /// Creates project enumeration options.
    /// </summary>
    /// <param name="rootPath">Root path to scan.</param>
    /// <param name="kind">Project kind to derive default patterns from.</param>
    /// <param name="customExtensions">Optional custom patterns overriding defaults.</param>
    /// <param name="excludeDirectories">Directory names to skip during traversal.</param>
    public ProjectEnumeration(string rootPath, ProjectKind kind, IEnumerable<string>? customExtensions, IEnumerable<string>? excludeDirectories)
    {
        RootPath = System.IO.Path.GetFullPath(rootPath.Trim().Trim('"'));
        Kind = kind;
        CustomExtensions = customExtensions?.ToArray();
        ExcludeDirectories = (excludeDirectories ?? new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" }).ToArray();
    }
}

/// <summary>Per-file conversion outcome.</summary>
public sealed class FileConversion
{
    /// <summary>File path that was inspected/converted.</summary>
    public string Path { get; }
    /// <summary>Detected source encoding name (WebName), if available.</summary>
    public string? Source { get; }
    /// <summary>Target encoding name (WebName) or target line ending label.</summary>
    public string Target { get; }
    /// <summary>Operation outcome: Converted, Skipped, or Error.</summary>
    public string Status { get; }
    /// <summary>Backup file path if backup was created.</summary>
    public string? BackupPath { get; }
    /// <summary>Error message when Status is Error.</summary>
    public string? Error { get; }
    /// <summary>
    /// Constructs a new per-file conversion record.
    /// </summary>
    public FileConversion(string path, string? source, string target, string status, string? backupPath, string? error)
    { Path = path; Source = source; Target = target; Status = status; BackupPath = backupPath; Error = error; }
}

/// <summary>Aggregate result for a project-wide conversion.</summary>
public sealed class ProjectConversionResult
{
    /// <summary>Total number of files considered.</summary>
    public int Total { get; }
    /// <summary>Number of files converted successfully.</summary>
    public int Converted { get; }
    /// <summary>Number of files skipped due to conditions/options.</summary>
    public int Skipped { get; }
    /// <summary>Number of files that failed to convert.</summary>
    public int Errors { get; }
    /// <summary>Per-file outcomes for the run.</summary>
    public IReadOnlyList<FileConversion> Files { get; }
    /// <summary>
    /// Creates an aggregate project conversion result.
    /// </summary>
    public ProjectConversionResult(int total, int converted, int skipped, int errors, IReadOnlyList<FileConversion> files)
    { Total = total; Converted = converted; Skipped = skipped; Errors = errors; Files = files; }
}

/// <summary>Options for encoding conversion across a project.</summary>
public sealed class EncodingConversionOptions
{
    /// <summary>Enumeration options that control which files are included.</summary>
    public ProjectEnumeration Enumeration { get; }
    /// <summary>Expected source encoding; when Any, any non-target encoding may be converted.</summary>
    public TextEncodingKind SourceEncoding { get; }
    /// <summary>Explicit target encoding; when null, defaults are chosen based on file type.</summary>
    public TextEncodingKind? TargetEncoding { get; }
    /// <summary>Whether to create backups prior to conversion.</summary>
    public bool CreateBackups { get; }
    /// <summary>Root folder for mirrored backups; when null, .bak is used next to files.</summary>
    public string? BackupDirectory { get; }
    /// <summary>Force conversion even if detection does not match SourceEncoding.</summary>
    public bool Force { get; }
    /// <summary>Do not rollback from backup if verification mismatch occurs.</summary>
    public bool NoRollbackOnMismatch { get; }
    /// <summary>Prefer UTF-8 BOM when writing PowerShell files.</summary>
    public bool PreferUtf8BomForPowerShell { get; }
    /// <summary>
    /// Creates encoding conversion options.
    /// </summary>
    public EncodingConversionOptions(ProjectEnumeration enumeration, TextEncodingKind sourceEncoding, TextEncodingKind? targetEncoding, bool createBackups, string? backupDirectory, bool force, bool noRollbackOnMismatch, bool preferUtf8BomForPowerShell = true)
    { Enumeration = enumeration; SourceEncoding = sourceEncoding; TargetEncoding = targetEncoding; CreateBackups = createBackups; BackupDirectory = backupDirectory; Force = force; NoRollbackOnMismatch = noRollbackOnMismatch; PreferUtf8BomForPowerShell = preferUtf8BomForPowerShell; }
}

/// <summary>Options for line-ending conversion across a project.</summary>
public sealed class LineEndingConversionOptions
{
    /// <summary>Enumeration options that control which files are included.</summary>
    public ProjectEnumeration Enumeration { get; }
    /// <summary>Target line ending style.</summary>
    public LineEnding Target { get; }
    /// <summary>Create backups prior to conversion.</summary>
    public bool CreateBackups { get; }
    /// <summary>Root folder for mirrored backups; when null, .bak is used next to files.</summary>
    public string? BackupDirectory { get; }
    /// <summary>Force conversion even if file already matches the target style.</summary>
    public bool Force { get; }
    /// <summary>Only convert files detected with mixed line endings.</summary>
    public bool OnlyMixed { get; }
    /// <summary>Ensure a final newline exists at end of the file.</summary>
    public bool EnsureFinalNewline { get; }
    /// <summary>Only modify files missing the final newline.</summary>
    public bool OnlyMissingNewline { get; }
    /// <summary>Prefer UTF-8 BOM when writing PowerShell files.</summary>
    public bool PreferUtf8BomForPowerShell { get; }
    /// <summary>
    /// Creates line ending conversion options.
    /// </summary>
    public LineEndingConversionOptions(ProjectEnumeration enumeration, LineEnding target, bool createBackups, string? backupDirectory, bool force, bool onlyMixed, bool ensureFinalNewline, bool onlyMissingNewline, bool preferUtf8BomForPowerShell = true)
    { Enumeration = enumeration; Target = target; CreateBackups = createBackups; BackupDirectory = backupDirectory; Force = force; OnlyMixed = onlyMixed; EnsureFinalNewline = ensureFinalNewline; OnlyMissingNewline = onlyMissingNewline; PreferUtf8BomForPowerShell = preferUtf8BomForPowerShell; }
}
