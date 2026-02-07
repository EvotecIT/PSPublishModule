namespace PowerForge.Web;

/// <summary>Controls documentation versioning behavior.</summary>
public sealed class VersioningSpec
{
    /// <summary>When true, versioned docs are enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base path for versioned docs (e.g. /docs).</summary>
    public string? BasePath { get; set; }

    /// <summary>Current version key.</summary>
    public string? Current { get; set; }

    /// <summary>Known documentation versions.</summary>
    public VersionSpec[] Versions { get; set; } = Array.Empty<VersionSpec>();
}

/// <summary>Represents a documentation version entry.</summary>
public sealed class VersionSpec
{
    /// <summary>Version identifier (e.g. v2).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display label.</summary>
    public string? Label { get; set; }

    /// <summary>URL for the version root.</summary>
    public string? Url { get; set; }

    /// <summary>Marks the default version.</summary>
    public bool Default { get; set; }

    /// <summary>Marks the latest version.</summary>
    public bool Latest { get; set; }

    /// <summary>Marks the version as deprecated.</summary>
    public bool Deprecated { get; set; }
}

/// <summary>Link checking configuration.</summary>
public sealed class LinkCheckSpec
{
    /// <summary>When true, link checking is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>When true, check external links.</summary>
    public bool IncludeExternal { get; set; }

    /// <summary>Optional glob patterns to skip.</summary>
    public string[] Skip { get; set; } = Array.Empty<string>();
}

/// <summary>Build cache configuration.</summary>
public sealed class BuildCacheSpec
{
    /// <summary>When true, caching is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Cache root directory.</summary>
    public string? Root { get; set; }

    /// <summary>Cache mode (contenthash, mtime).</summary>
    public string? Mode { get; set; }
}

/// <summary>Verification policy controls shared by CLI and pipeline verify/doctor commands.</summary>
public sealed class VerifyPolicySpec
{
    /// <summary>When true, verify fails when any warning is emitted.</summary>
    public bool FailOnWarnings { get; set; }

    /// <summary>When true, verify fails when navigation lint warnings are emitted.</summary>
    public bool FailOnNavLint { get; set; }

    /// <summary>When true, verify fails when theme contract warnings are emitted.</summary>
    public bool FailOnThemeContract { get; set; }
}
