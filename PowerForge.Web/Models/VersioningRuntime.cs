namespace PowerForge.Web;

/// <summary>Resolved runtime model for documentation version navigation.</summary>
public sealed class VersioningRuntime
{
    /// <summary>True when versioning is enabled and has at least one version entry.</summary>
    public bool Enabled { get; set; }

    /// <summary>Normalized base path for versioned docs (for example /docs).</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Resolved current version entry.</summary>
    public VersionRuntimeItem? Current { get; set; }

    /// <summary>Resolved latest version entry.</summary>
    public VersionRuntimeItem? Latest { get; set; }

    /// <summary>Resolved LTS version entry.</summary>
    public VersionRuntimeItem? Lts { get; set; }

    /// <summary>Resolved default version entry.</summary>
    public VersionRuntimeItem? Default { get; set; }

    /// <summary>Resolved version list.</summary>
    public VersionRuntimeItem[] Versions { get; set; } = Array.Empty<VersionRuntimeItem>();
}

/// <summary>Resolved version entry with computed state for templates.</summary>
public sealed class VersionRuntimeItem
{
    /// <summary>Version identifier (for example v2).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Resolved URL for the version root.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>True when this version is marked as default.</summary>
    public bool Default { get; set; }

    /// <summary>True when this version is marked as latest.</summary>
    public bool Latest { get; set; }

    /// <summary>True when this version is marked as LTS.</summary>
    public bool Lts { get; set; }

    /// <summary>True when this version is marked as deprecated.</summary>
    public bool Deprecated { get; set; }

    /// <summary>True when this version is selected as current for the rendered page.</summary>
    public bool IsCurrent { get; set; }
}
