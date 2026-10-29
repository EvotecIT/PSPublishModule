namespace PowerForge;

/// <summary>Selects Markdown documents whose marked code blocks are synchronized from local source.</summary>
public sealed class MarkdownCodeSyncSpec
{
    /// <summary>Repository root. Configuration hosts resolve relative values against the configuration directory.</summary>
    public string RootPath { get; set; } = ".";

    /// <summary>Explicit Markdown file paths relative to the root. Source paths in markers also use this root.</summary>
    public string[] Files { get; set; } = System.Array.Empty<string>();
}

/// <summary>Describes a successful check or update of source-backed Markdown code blocks.</summary>
public sealed class MarkdownCodeSyncResult
{
    /// <summary>Number of selected Markdown files checked.</summary>
    public int FileCount { get; set; }
    /// <summary>Number of source-backed blocks checked.</summary>
    public int BlockCount { get; set; }
    /// <summary>Root-relative files whose generated blocks differ from their sources.</summary>
    public string[] ChangedFiles { get; set; } = System.Array.Empty<string>();
    /// <summary>Whether this operation applied updates rather than checking only.</summary>
    public bool Applied { get; set; }
}
