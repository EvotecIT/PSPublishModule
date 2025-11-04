namespace PSMaintenance;

/// <summary>
/// High-level selection for which documents to display.
/// </summary>
public enum DocumentationSelection
{
    /// <summary>Default selection: README, CHANGELOG, LICENSE, Upgrade (if present) and Intro (if configured).</summary>
    Default,
    /// <summary>Show Introduction (IntroText/IntroFile) only.</summary>
    Intro,
    /// <summary>Show README only.</summary>
    Readme,
    /// <summary>Show CHANGELOG only.</summary>
    Changelog,
    /// <summary>Show LICENSE only.</summary>
    License,
    /// <summary>Show Upgrade information only.</summary>
    Upgrade,
    /// <summary>Show all standard docs (Intro, README, CHANGELOG, LICENSE).</summary>
    All,
    /// <summary>Show only ImportantLinks list (if configured).</summary>
    Links
}

