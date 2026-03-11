using System.Collections;
using System.Management.Automation;

namespace PowerForge;

internal sealed class ModuleBuildPreparationRequest
{
    public string ParameterSetName { get; set; } = string.Empty;
    public ScriptBlock? Settings { get; set; }
    public IDictionary Configuration { get; set; } = new Hashtable();
    public string? ModuleName { get; set; }
    public string? InputPath { get; set; }
    public string? StagingPath { get; set; }
    public string? CsprojPath { get; set; }
    public string DotNetConfiguration { get; set; } = "Release";
    public string[] DotNetFramework { get; set; } = Array.Empty<string>();
    public bool DotNetFrameworkWasBound { get; set; }
    public bool Legacy { get; set; }
    public bool SkipInstall { get; set; }
    public InstallationStrategy InstallStrategy { get; set; }
    public bool InstallStrategyWasBound { get; set; }
    public int KeepVersions { get; set; } = 3;
    public bool KeepVersionsWasBound { get; set; }
    public string[]? InstallRoots { get; set; }
    public bool InstallRootsWasBound { get; set; }
    public LegacyFlatModuleHandling LegacyFlatHandling { get; set; } = LegacyFlatModuleHandling.Warn;
    public bool LegacyFlatHandlingWasBound { get; set; }
    public string[]? PreserveInstallVersions { get; set; }
    public bool PreserveInstallVersionsWasBound { get; set; }
    public bool KeepStaging { get; set; }
    public string[] ExcludeDirectories { get; set; } = Array.Empty<string>();
    public string[] ExcludeFiles { get; set; } = Array.Empty<string>();
    public string? DiagnosticsBaselinePath { get; set; }
    public bool GenerateDiagnosticsBaseline { get; set; }
    public bool UpdateDiagnosticsBaseline { get; set; }
    public bool FailOnNewDiagnostics { get; set; }
    public BuildDiagnosticSeverity? FailOnDiagnosticsSeverity { get; set; }
    public string[]? DiagnosticsBinaryConflictSearchRoot { get; set; }
    public bool JsonOnly { get; set; }
    public string? JsonPath { get; set; }
    public string CurrentPath { get; set; } = string.Empty;
    public string? ScriptRoot { get; set; }
    public Func<string, string>? ResolvePath { get; set; }
}
