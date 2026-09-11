namespace PowerForge;

/// <summary>A bounded process invocation and its observable contract.</summary>
public sealed class ReleaseCommandValidation
{
    /// <summary>Human-readable check name.</summary>
    public string Name { get; set; } = "Command";
    /// <summary>Executable path or name. Supports validation variables.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Structured arguments, without shell interpolation.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();
    /// <summary>Working directory; defaults to the product root.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>Maximum runtime in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 300;
    /// <summary>Expected process exit code; null leaves exit-code interpretation to a product probe. Timeouts and output limits still fail.</summary>
    public int? ExpectedExitCode { get; set; } = 0;
    /// <summary>Optional exact trimmed standard output.</summary>
    public string? ExpectedOutput { get; set; }
    /// <summary>Literal strings required in standard output.</summary>
    public string[] OutputContains { get; set; } = Array.Empty<string>();
    /// <summary>Optional JSON output kind: Array, Object, String, Number, True, False, or Null.</summary>
    public string? OutputJsonKind { get; set; }
    /// <summary>Optional minimum number of elements when JSON output is an array.</summary>
    public int? MinimumJsonItems { get; set; }
    /// <summary>Files that must exist and contain data after execution.</summary>
    public string[] NonEmptyFiles { get; set; } = Array.Empty<string>();
    /// <summary>Child environment overrides.</summary>
    public Dictionary<string, string?> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional supported platforms: Windows, Linux, OSX. Empty runs everywhere.</summary>
    public string[] Platforms { get; set; } = Array.Empty<string>();
}

/// <summary>Installs an exact local .NET tool package into an isolated directory and runs its probes.</summary>
public sealed class DotNetToolValidation
{
    /// <summary>Package identity.</summary>
    public string PackageId { get; set; } = string.Empty;
    /// <summary>Local package directory.</summary>
    public string PackageRoot { get; set; } = "{PackageRoot}";
    /// <summary>Installed tool command, without a platform extension.</summary>
    public string CommandName { get; set; } = string.Empty;
    /// <summary>Test manifest-local installation in addition to --tool-path.</summary>
    public bool IncludeManifestInstall { get; set; }
    /// <summary>Commands with {ToolPath}, {WorkRoot}, {Version}, and {ProjectRoot} variables.</summary>
    public ReleaseCommandValidation[] Commands { get; set; } = Array.Empty<ReleaseCommandValidation>();
}

/// <summary>Runs a product-owned project against only the exact staged first-party packages.</summary>
public sealed class PackageConsumerValidation
{
    /// <summary>Directory copied to an isolated workspace before restore.</summary>
    public string SourceDirectory { get; set; } = string.Empty;
    /// <summary>Project file relative to SourceDirectory.</summary>
    public string ProjectFile { get; set; } = string.Empty;
    /// <summary>Frameworks to execute on all platforms.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();
    /// <summary>Additional frameworks executed on Windows.</summary>
    public string[] WindowsFrameworks { get; set; } = Array.Empty<string>();
    /// <summary>Additional public feeds used only for dependencies outside the declared package set.</summary>
    public string[] DependencySources { get; set; } = new[] { "https://api.nuget.org/v3/index.json" };
}

/// <summary>Checks packaged module files and invokes product probes in isolated PowerShell hosts.</summary>
public sealed class ModuleArtifactValidation
{
    /// <summary>Module ZIP path or module directory.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Module directory within the archive; empty for a directory input.</summary>
    public string ArchiveRoot { get; set; } = string.Empty;
    /// <summary>Manifest file relative to the module directory.</summary>
    public string Manifest { get; set; } = string.Empty;
    /// <summary>Required module files, supporting glob patterns.</summary>
    public string[] RequiredFiles { get; set; } = Array.Empty<string>();
    /// <summary>Assembly patterns whose three-part versions must match the release.</summary>
    public string[] VersionedAssemblies { get; set; } = Array.Empty<string>();
    /// <summary>Optional expected ProcessorArchitecture manifest value.</summary>
    public string? ProcessorArchitecture { get; set; }
    /// <summary>Optional product-owned script, executed with POWERFORGE_MODULE_PATH and POWERFORGE_TEST_ROOT.</summary>
    public string? ProbeScript { get; set; }
    /// <summary>PowerShell hosts to run, using executable names or paths.</summary>
    public string[] Hosts { get; set; } = new[] { "pwsh" };
    /// <summary>Optional payload signature requirements.</summary>
    public PayloadSignatureValidation? Signatures { get; set; }
}

/// <summary>Authenticode requirements for a file set.</summary>
public sealed class PayloadSignatureValidation
{
    /// <summary>Relative file patterns to inspect.</summary>
    public string[] Include { get; set; } = new[] { "**/*.dll", "**/*.ps1", "**/*.psm1", "**/*.psd1" };
    /// <summary>Optional allowed signer certificate thumbprints.</summary>
    public string[] Thumbprints { get; set; } = Array.Empty<string>();
}

/// <summary>Expected artifacts from a CLI publish matrix.</summary>
public sealed class CliArtifactValidation
{
    /// <summary>Unified release or dotnet publish manifest path.</summary>
    public string ManifestPath { get; set; } = "{ReleaseManifestPath}";
    /// <summary>Optional publish config used as the authoritative runtime/style matrix.</summary>
    public string? PublishConfigPath { get; set; }
    /// <summary>Expected CLI target identity.</summary>
    public string Target { get; set; } = string.Empty;
    /// <summary>Expected runtime identifiers when no publish configuration is supplied.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();
    /// <summary>Optional framework matrix when no publish configuration is supplied.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();
    /// <summary>Expected publish styles when no publish configuration is supplied.</summary>
    public string[] Styles { get; set; } = Array.Empty<string>();
    /// <summary>Allow only tool and metadata entries in a unified manifest.</summary>
    public bool ToolsOnly { get; set; }
}
