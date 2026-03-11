namespace PowerForge;

internal sealed class DocumentationConfigurationRequest
{
    public bool Enable { get; set; }
    public bool StartClean { get; set; }
    public bool UpdateWhenNew { get; set; }
    public bool SyncExternalHelpToProjectRoot { get; set; }
    public bool SkipExternalHelp { get; set; }
    public bool SkipAboutTopics { get; set; }
    public bool SkipFallbackExamples { get; set; }
    public string ExternalHelpCulture { get; set; } = "en-US";
    public string ExternalHelpFileName { get; set; } = string.Empty;
    public bool ExternalHelpCultureSpecified { get; set; }
    public bool ExternalHelpFileNameSpecified { get; set; }
    public string[] AboutTopicsSourcePath { get; set; } = Array.Empty<string>();
    public bool AboutTopicsSourcePathSpecified { get; set; }
    public string Path { get; set; } = string.Empty;
    public string PathReadme { get; set; } = string.Empty;
}
