using System.Collections;

namespace PowerForge;

internal sealed class ModuleBuildProfileRequest
{
    public ModuleBuildProfileKind Profile { get; set; } = ModuleBuildProfileKind.Standard;
    public bool Documentation { get; set; } = true;
    public string DocumentationPath { get; set; } = "Docs";
    public string DocumentationReadmePath { get; set; } = @"Docs\Readme.md";
    public bool SyncExternalHelpToProjectRoot { get; set; } = true;
    public string[] AboutTopicsSourcePath { get; set; } = new[] { @"Help\About" };
    public bool Validation { get; set; } = true;
    public bool EnableScriptAnalyzer { get; set; } = true;
    public bool FileConsistency { get; set; } = true;
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;
    public string[] FileConsistencyExcludeDirectories { get; set; } = new[] { "Build", "Docs", "Documentation", "Examples", "Tests" };
    public Hashtable? EncodingOverrides { get; set; }
    public bool Compatibility { get; set; } = true;
    public int MinimumCompatibilityPercentage { get; set; } = 95;
    public bool ImportSelf { get; set; } = true;
    public bool ImportRequiredModules { get; set; }
    public bool MergeModuleOnBuild { get; set; } = true;
    public bool MergeFunctionsFromApprovedModulesSpecified { get; set; }
    public bool MergeFunctionsFromApprovedModules { get; set; }
    public bool SignModule { get; set; }
    public string? CertificateThumbprint { get; set; }
    public bool SkipBuiltinReplacements { get; set; }
    public bool DoNotAttemptToFixRelativePaths { get; set; }
    public bool DotSourceLibraries { get; set; }
    public bool DotSourceClasses { get; set; }
    public bool InstallMissingModules { get; set; } = true;
    public InstallationStrategy VersionedInstallStrategy { get; set; } = InstallationStrategy.AutoRevision;
    public int VersionedInstallKeep { get; set; } = 3;
    public bool KillLockersBeforeInstall { get; set; }
    public bool KillLockersForce { get; set; }
    public string? NETProjectPath { get; set; }
    public string? NETProjectName { get; set; }
    public string NETConfiguration { get; set; } = "Release";
    public string[] NETFramework { get; set; } = Array.Empty<string>();
    public bool NETHandleAssemblyWithSameName { get; set; }
    public bool NETAssemblyLoadContext { get; set; }
    public bool ResolveBinaryConflicts { get; set; }
    public string? ResolveBinaryConflictsName { get; set; }
    public AssemblyTypeAcceleratorExportMode? NETAssemblyTypeAcceleratorMode { get; set; }
    public string[]? NETAssemblyTypeAccelerators { get; set; }
    public string[]? NETAssemblyTypeAcceleratorAssemblies { get; set; }
    public bool SignIncludeBinariesSpecified { get; set; }
    public bool SignIncludeBinaries { get; set; }
    public bool SignIncludeInternalsSpecified { get; set; }
    public bool SignIncludeInternals { get; set; }
    public bool SignIncludeExeSpecified { get; set; }
    public bool SignIncludeExe { get; set; }
}
