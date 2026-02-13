namespace PowerForge.Web;

/// <summary>Options for package hub generation.</summary>
public sealed class WebPackageHubOptions
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }
    /// <summary>Optional title override.</summary>
    public string? Title { get; set; }
    /// <summary>Project file paths (.csproj).</summary>
    public List<string> ProjectPaths { get; set; } = new();
    /// <summary>PowerShell module manifest paths (.psd1).</summary>
    public List<string> ModulePaths { get; set; } = new();
}

/// <summary>Result payload for package hub generation.</summary>
public sealed class WebPackageHubResult
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of .NET libraries included.</summary>
    public int LibraryCount { get; set; }
    /// <summary>Number of PowerShell modules included.</summary>
    public int ModuleCount { get; set; }
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Unified package/module hub document.</summary>
public sealed class WebPackageHubDocument
{
    /// <summary>Document title.</summary>
    public string Title { get; set; } = "Package Hub";
    /// <summary>Generation timestamp in UTC.</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;
    /// <summary>Library records discovered from project files.</summary>
    public List<WebPackageHubLibrary> Libraries { get; set; } = new();
    /// <summary>Module records discovered from module manifests.</summary>
    public List<WebPackageHubModule> Modules { get; set; } = new();
    /// <summary>Warnings emitted during generation.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Represents a discovered .NET library.</summary>
public sealed class WebPackageHubLibrary
{
    /// <summary>Source project path (relative when possible).</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Project display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Package identifier.</summary>
    public string? PackageId { get; set; }
    /// <summary>Library version.</summary>
    public string? Version { get; set; }
    /// <summary>Short description.</summary>
    public string? Description { get; set; }
    /// <summary>Author string.</summary>
    public string? Authors { get; set; }
    /// <summary>Repository URL.</summary>
    public string? RepositoryUrl { get; set; }
    /// <summary>Target frameworks.</summary>
    public List<string> TargetFrameworks { get; set; } = new();
    /// <summary>NuGet package references.</summary>
    public List<WebPackageHubDependency> Dependencies { get; set; } = new();
}

/// <summary>Represents a discovered PowerShell module.</summary>
public sealed class WebPackageHubModule
{
    /// <summary>Source module manifest path (relative when possible).</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Module name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Module version.</summary>
    public string? Version { get; set; }
    /// <summary>Short description.</summary>
    public string? Description { get; set; }
    /// <summary>Module author.</summary>
    public string? Author { get; set; }
    /// <summary>Minimum PowerShell version.</summary>
    public string? PowerShellVersion { get; set; }
    /// <summary>Compatible PowerShell editions.</summary>
    public List<string> CompatiblePSEditions { get; set; } = new();
    /// <summary>Exported cmdlets/functions.</summary>
    public List<string> ExportedCommands { get; set; } = new();
    /// <summary>Required module dependencies.</summary>
    public List<WebPackageHubDependency> RequiredModules { get; set; } = new();
}

/// <summary>Simple dependency record.</summary>
public sealed class WebPackageHubDependency
{
    /// <summary>Dependency name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Dependency version constraint when known.</summary>
    public string? Version { get; set; }
}
