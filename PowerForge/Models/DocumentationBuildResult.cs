namespace PowerForge;

/// <summary>
/// Result of generating documentation (markdown help) for a module.
/// </summary>
public sealed class DocumentationBuildResult
{
    /// <summary>True when documentation generation was enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Tool used to generate documentation.</summary>
    public DocumentationTool Tool { get; }

    /// <summary>Full path to the docs output folder.</summary>
    public string DocsPath { get; }

    /// <summary>Full path to the module/readme markdown file.</summary>
    public string ReadmePath { get; }

    /// <summary>True when generation succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Exit code returned by the out-of-process tool runner.</summary>
    public int ExitCode { get; }

    /// <summary>Number of markdown files found under <see cref="DocsPath"/> after generation.</summary>
    public int MarkdownFiles { get; }

    /// <summary>
    /// Full path to the generated external help MAML XML file (e.g. <c>en-US\ModuleName-help.xml</c>).
    /// Empty when external help was not generated.
    /// </summary>
    public string ExternalHelpFilePath { get; }

    /// <summary>Optional error message when <see cref="Succeeded"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public DocumentationBuildResult(
        bool enabled,
        DocumentationTool tool,
        string docsPath,
        string readmePath,
        bool succeeded,
        int exitCode,
        int markdownFiles,
        string externalHelpFilePath,
        string? errorMessage)
    {
        Enabled = enabled;
        Tool = tool;
        DocsPath = docsPath;
        ReadmePath = readmePath;
        Succeeded = succeeded;
        ExitCode = exitCode;
        MarkdownFiles = markdownFiles;
        ExternalHelpFilePath = externalHelpFilePath ?? string.Empty;
        ErrorMessage = errorMessage;
    }
}
