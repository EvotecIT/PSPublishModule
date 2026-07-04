namespace PowerForge;

/// <summary>
/// Metadata stored in a PSResourceGet-compatible <c>PSScriptInfo</c> block.
/// </summary>
public sealed class ManagedScriptFileInfo
{
    /// <summary>Script file name without extension.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full script path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Script version string.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Script metadata GUID.</summary>
    public Guid Guid { get; set; }

    /// <summary>Script author.</summary>
    public string? Author { get; set; }

    /// <summary>Company name.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Copyright text.</summary>
    public string? Copyright { get; set; }

    /// <summary>Search tags.</summary>
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    /// <summary>When used as update input, indicates that <see cref="Tags"/> was explicitly supplied.</summary>
    public bool TagsSpecified { get; set; }

    /// <summary>License URI.</summary>
    public string? LicenseUri { get; set; }

    /// <summary>Project URI.</summary>
    public string? ProjectUri { get; set; }

    /// <summary>Icon URI.</summary>
    public string? IconUri { get; set; }

    /// <summary>External module dependencies declared in the metadata block.</summary>
    public IReadOnlyList<string> ExternalModuleDependencies { get; set; } = Array.Empty<string>();

    /// <summary>When used as update input, indicates that <see cref="ExternalModuleDependencies"/> was explicitly supplied.</summary>
    public bool ExternalModuleDependenciesSpecified { get; set; }

    /// <summary>Required scripts declared in the metadata block.</summary>
    public IReadOnlyList<string> RequiredScripts { get; set; } = Array.Empty<string>();

    /// <summary>When used as update input, indicates that <see cref="RequiredScripts"/> was explicitly supplied.</summary>
    public bool RequiredScriptsSpecified { get; set; }

    /// <summary>External script dependencies declared in the metadata block.</summary>
    public IReadOnlyList<string> ExternalScriptDependencies { get; set; } = Array.Empty<string>();

    /// <summary>When used as update input, indicates that <see cref="ExternalScriptDependencies"/> was explicitly supplied.</summary>
    public bool ExternalScriptDependenciesSpecified { get; set; }

    /// <summary>Release notes text.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Private data text.</summary>
    public string? PrivateData { get; set; }

    /// <summary>Comment-based help description.</summary>
    public string? Description { get; set; }

    /// <summary>Required modules declared as <c>#Requires -Module</c> statements.</summary>
    public IReadOnlyList<ManagedScriptRequiredModule> RequiredModules { get; set; } = Array.Empty<ManagedScriptRequiredModule>();

    /// <summary>When used as update input, indicates that <see cref="RequiredModules"/> was explicitly supplied.</summary>
    public bool RequiredModulesSpecified { get; set; }

    /// <summary>Original script-level comment-based help block, when present.</summary>
    public string? ScriptHelp { get; set; }

    /// <summary>Script body after metadata, requires, and help blocks.</summary>
    public string? ScriptContent { get; set; }
}
