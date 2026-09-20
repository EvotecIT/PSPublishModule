namespace PowerForge;

/// <summary>Display-only publication settings without credential values or credential references.</summary>
public sealed class ProjectPublicationPreview
{
    /// <summary>Whether project configuration enables NuGet publication.</summary>
    public bool PublishNuGet { get; internal set; }
    /// <summary>Whether project configuration enables GitHub publication.</summary>
    public bool PublishGitHub { get; internal set; }
    /// <summary>Configured NuGet destination with URI credentials, query and fragment removed.</summary>
    public string NuGetDestination { get; internal set; } = string.Empty;
    /// <summary>Whether the displayed destination omits URI components.</summary>
    public bool DestinationRedacted { get; internal set; }
    /// <summary>Configured GitHub owner and repository, without credentials.</summary>
    public string GitHubRepository { get; internal set; } = string.Empty;
}
