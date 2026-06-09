namespace PowerForge.Web;

/// <summary>Options for version hub generation.</summary>
public sealed class WebVersionHubOptions
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }
    /// <summary>Optional title override.</summary>
    public string? Title { get; set; }
    /// <summary>Optional version entries supplied explicitly.</summary>
    public List<WebVersionHubEntryInput> Entries { get; set; } = new();
    /// <summary>Optional directory for discovering version folders.</summary>
    public string? DiscoverRoot { get; set; }
    /// <summary>Folder pattern used when discovering versions.</summary>
    public string DiscoverPattern { get; set; } = "v*";
    /// <summary>Base route used when building discovered version paths.</summary>
    public string BasePath { get; set; } = "/docs/";
    /// <summary>When true and no explicit latest is set, newest discovered/declared version becomes latest.</summary>
    public bool SetLatestFromNewest { get; set; } = true;
}

/// <summary>Input entry describing a single documentation version.</summary>
public sealed class WebVersionHubEntryInput
{
    /// <summary>Stable id for version entry.</summary>
    public string? Id { get; set; }
    /// <summary>Version token (for example 2.1 or v2.1).</summary>
    public string? Version { get; set; }
    /// <summary>Optional display label.</summary>
    public string? Label { get; set; }
    /// <summary>Route path for this version.</summary>
    public string? Path { get; set; }
    /// <summary>Optional release channel (stable, preview, nightly).</summary>
    public string? Channel { get; set; }
    /// <summary>Optional support descriptor (LTS, maintenance, EOL).</summary>
    public string? Support { get; set; }
    /// <summary>True when this is latest/default docs version.</summary>
    public bool Latest { get; set; }
    /// <summary>True when this version is an LTS channel.</summary>
    public bool Lts { get; set; }
    /// <summary>True when this version is deprecated.</summary>
    public bool Deprecated { get; set; }
    /// <summary>Optional aliases (for example latest, stable).</summary>
    public List<string> Aliases { get; set; } = new();
}

/// <summary>Result payload for version hub generation.</summary>
public sealed class WebVersionHubResult
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Total number of versions written.</summary>
    public int VersionCount { get; set; }
    /// <summary>Latest version id (if available).</summary>
    public string? LatestVersion { get; set; }
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Version hub document written to JSON.</summary>
public sealed class WebVersionHubDocument
{
    /// <summary>Document title.</summary>
    public string Title { get; set; } = "Version Hub";
    /// <summary>Generation timestamp (UTC).</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;
    /// <summary>Path for the latest docs version when available.</summary>
    public string? LatestPath { get; set; }
    /// <summary>Path for the LTS docs version when available.</summary>
    public string? LtsPath { get; set; }
    /// <summary>Ordered version entries.</summary>
    public List<WebVersionHubEntry> Versions { get; set; } = new();
    /// <summary>Warnings emitted during generation.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Resolved version entry emitted to JSON.</summary>
public sealed class WebVersionHubEntry
{
    /// <summary>Stable id for version entry.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Version token.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Route path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Optional release channel.</summary>
    public string? Channel { get; set; }
    /// <summary>Optional support descriptor.</summary>
    public string? Support { get; set; }
    /// <summary>True when latest/default.</summary>
    public bool Latest { get; set; }
    /// <summary>True when LTS.</summary>
    public bool Lts { get; set; }
    /// <summary>True when deprecated.</summary>
    public bool Deprecated { get; set; }
    /// <summary>Optional aliases.</summary>
    public List<string> Aliases { get; set; } = new();
}