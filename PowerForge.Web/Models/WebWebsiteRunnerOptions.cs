namespace PowerForge.Web;

/// <summary>Options for running a website pipeline from either source or published binaries.</summary>
public sealed class WebWebsiteRunnerOptions
{
    /// <summary>Website repository root.</summary>
    public string WebsiteRoot { get; set; } = string.Empty;

    /// <summary>Pipeline config path.</summary>
    public string PipelineConfig { get; set; } = string.Empty;

    /// <summary>Runner mode: source or binary.</summary>
    public string EngineMode { get; set; } = "source";

    /// <summary>Pipeline mode, usually ci or dev.</summary>
    public string PipelineMode { get; set; } = "ci";

    /// <summary>Optional engine lock path override.</summary>
    public string? PowerForgeLockPath { get; set; }

    /// <summary>Optional explicit engine repository.</summary>
    public string? PowerForgeRepository { get; set; }

    /// <summary>Optional explicit engine ref.</summary>
    public string? PowerForgeRef { get; set; }

    /// <summary>Optional repository override after lock resolution.</summary>
    public string? PowerForgeRepositoryOverride { get; set; }

    /// <summary>Optional ref override after lock resolution.</summary>
    public string? PowerForgeRefOverride { get; set; }

    /// <summary>Optional tool lock path override.</summary>
    public string? PowerForgeToolLockPath { get; set; }

    /// <summary>Optional GitHub token used for downloads.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Optional temporary directory root.</summary>
    public string? RunnerTempPath { get; set; }

    /// <summary>Optional note to mirror maintenance workflow messaging.</summary>
    public bool MaintenanceModeNote { get; set; }
}
