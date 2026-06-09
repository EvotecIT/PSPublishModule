namespace PowerForge.Web;

/// <summary>Options for generating compatibility matrix data.</summary>
public sealed class WebCompatibilityMatrixOptions
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Optional markdown output path.</summary>
    public string? MarkdownOutputPath { get; set; }

    /// <summary>Optional base directory for resolving relative input paths.</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>Optional document title.</summary>
    public string? Title { get; set; }

    /// <summary>When true, include dependency details in output.</summary>
    public bool IncludeDependencies { get; set; } = true;

    /// <summary>Optional explicit entries.</summary>
    public List<WebCompatibilityMatrixEntryInput> Entries { get; set; } = new();

    /// <summary>Optional .csproj inputs to infer NuGet/.NET compatibility.</summary>
    public List<string> CsprojFiles { get; set; } = new();

    /// <summary>Optional .psd1 inputs to infer PowerShell module compatibility.</summary>
    public List<string> Psd1Files { get; set; } = new();
}

/// <summary>Input entry for compatibility matrix generation.</summary>
public sealed class WebCompatibilityMatrixEntryInput
{
    /// <summary>Entry type (for example nuget, powershell-module).</summary>
    public string? Type { get; set; }

    /// <summary>Package/module identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Optional display name.</summary>
    public string? Name { get; set; }

    /// <summary>Optional version.</summary>
    public string? Version { get; set; }

    /// <summary>Optional source path used to derive this entry.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Optional target frameworks (for .NET projects).</summary>
    public List<string> TargetFrameworks { get; set; } = new();

    /// <summary>Optional PowerShell editions (for modules).</summary>
    public List<string> PowerShellEditions { get; set; } = new();

    /// <summary>Optional minimum PowerShell version.</summary>
    public string? PowerShellVersion { get; set; }

    /// <summary>Optional dependencies.</summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Optional status (stable, preview, deprecated, etc.).</summary>
    public string? Status { get; set; }

    /// <summary>Optional notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Optional project/docs/package URL.</summary>
    public string? Url { get; set; }
}

/// <summary>Generator result payload.</summary>
public sealed class WebCompatibilityMatrixResult
{
    /// <summary>Resolved JSON output path.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Resolved markdown output path when generated.</summary>
    public string? MarkdownOutputPath { get; set; }

    /// <summary>Total emitted entries.</summary>
    public int EntryCount { get; set; }

    /// <summary>Generation warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Serialized compatibility matrix document.</summary>
public sealed class WebCompatibilityMatrixDocument
{
    /// <summary>Document title.</summary>
    public string Title { get; set; } = "Compatibility Matrix";

    /// <summary>Generation timestamp (UTC).</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;

    /// <summary>Compatibility entries.</summary>
    public List<WebCompatibilityMatrixEntry> Entries { get; set; } = new();
}

/// <summary>Compatibility matrix row.</summary>
public sealed class WebCompatibilityMatrixEntry
{
    /// <summary>Entry type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Version string.</summary>
    public string? Version { get; set; }

    /// <summary>Source path used to derive this entry.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Target frameworks (if any).</summary>
    public List<string> TargetFrameworks { get; set; } = new();

    /// <summary>PowerShell editions (if any).</summary>
    public List<string> PowerShellEditions { get; set; } = new();

    /// <summary>Minimum PowerShell version (if any).</summary>
    public string? PowerShellVersion { get; set; }

    /// <summary>Dependencies.</summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Status tag.</summary>
    public string? Status { get; set; }

    /// <summary>Additional notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Entry URL.</summary>
    public string? Url { get; set; }
}
