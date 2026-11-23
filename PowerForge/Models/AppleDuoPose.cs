namespace PowerForge;

/// <summary>iPhone Duo hinge pose that selects the display a capture uses.</summary>
public enum AppleDuoPose
{
    /// <summary>Folded; the outer display (simctl display "primary", 1398x2034) shows the interface.</summary>
    Closed,
    /// <summary>Unfolded; the inner display (simctl display "primary-1", 2007x2853) shows the interface.</summary>
    Open
}

/// <summary>Interface orientation accepted on the selected iPhone Duo display.</summary>
public enum AppleDuoOrientation
{
    /// <summary>Portrait or landscape, whichever the interface is in.</summary>
    Any,
    /// <summary>Portrait only.</summary>
    Portrait,
    /// <summary>Landscape only.</summary>
    Landscape
}

/// <summary>Puts an iPhone Duo simulator into a pose before the session's foreground command runs.</summary>
/// <remarks>
/// Xcode has no public command for the hinge. Without <see cref="FoldToolPath"/>, PowerForge asks the person to set the
/// pose in Device Hub and waits until the display is live. With it, PowerForge first tries the pinned duoctl release,
/// which relies on private simulator interfaces, and falls back to the same prompt when that doesn't produce the pose.
/// </remarks>
public sealed class AppleDuoPoseRequest
{
    /// <summary>Pose that must be live before the command starts.</summary>
    public AppleDuoPose Pose { get; set; }
    /// <summary>Accepted interface orientation on the live display.</summary>
    public AppleDuoOrientation Orientation { get; set; } = AppleDuoOrientation.Any;
    /// <summary>Opt-in path to the pinned duoctl executable; null asks a person to set the pose.</summary>
    public string? FoldToolPath { get; set; }
    /// <summary>How long to wait for a person to set the pose before failing the session.</summary>
    public TimeSpan PromptTimeout { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>How long one fold tool command may run, and how long its result may take to appear.</summary>
    public TimeSpan FoldTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>How the requested iPhone Duo pose was reached and what the live display reported.</summary>
public sealed class AppleDuoPoseResult
{
    /// <summary>Requested pose.</summary>
    public AppleDuoPose Pose { get; set; }
    /// <summary>simctl display name the capture must use.</summary>
    public string Display { get; set; } = string.Empty;
    /// <summary>Width in pixels of the live display's screenshot.</summary>
    public int Width { get; set; }
    /// <summary>Height in pixels of the live display's screenshot.</summary>
    public int Height { get; set; }
    /// <summary>portrait or landscape, from the screenshot's size.</summary>
    public string Orientation { get; set; } = string.Empty;
    /// <summary>already, fold-tool, or prompt.</summary>
    public string Method { get; set; } = string.Empty;
    /// <summary>Why the fold tool was skipped or didn't produce the pose; null when it wasn't configured or worked.</summary>
    public string? FoldToolNote { get; set; }
}
