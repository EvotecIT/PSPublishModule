using System;
using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Emits a reusable module build profile for common PowerForge module builds.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits the same configuration segment types as the lower-level
/// <c>New-ConfigurationFormat</c>, <c>New-ConfigurationDocumentation</c>,
/// <c>New-ConfigurationValidation</c>, <c>New-ConfigurationFileConsistency</c>,
/// <c>New-ConfigurationCompatibility</c>, <c>New-ConfigurationImportModule</c>, and
/// <c>New-ConfigurationBuild</c> commands. Use it when a module wrapper should stay thin
/// and only declare project-specific values.
/// </para>
/// </remarks>
/// <example>
/// <summary>Use standard script-module defaults</summary>
/// <code>New-ConfigurationModuleBuildProfile</code>
/// </example>
/// <example>
/// <summary>Use binary-module defaults with an owned .NET project</summary>
/// <code>New-ConfigurationModuleBuildProfile -Profile Binary -NETProjectName MyModule -NETProjectPath Sources\MyModule</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationModuleBuildProfile")]
public sealed class NewConfigurationModuleBuildProfileCommand : PSCmdlet
{
    /// <summary>Named profile to emit.</summary>
    [Parameter]
    public ModuleBuildProfileKind Profile { get; set; } = ModuleBuildProfileKind.Standard;

    /// <summary>Enable documentation generation defaults.</summary>
    [Parameter]
    public bool Documentation { get; set; } = true;

    /// <summary>Generated documentation path.</summary>
    [Parameter]
    public string DocumentationPath { get; set; } = "Docs";

    /// <summary>Readme path used by generated documentation.</summary>
    [Parameter]
    public string DocumentationReadmePath { get; set; } = @"Docs\Readme.md";

    /// <summary>Sync generated external help back to the project root.</summary>
    [Parameter]
    public bool SyncExternalHelpToProjectRoot { get; set; } = true;

    /// <summary>Source paths for about-topic files.</summary>
    [Parameter]
    public string[] AboutTopicsSourcePath { get; set; } = new[] { @"Help\About" };

    /// <summary>Enable module validation defaults.</summary>
    [Parameter]
    public bool Validation { get; set; } = true;

    /// <summary>Enable PSScriptAnalyzer as part of validation.</summary>
    [Parameter]
    public bool EnableScriptAnalyzer { get; set; } = true;

    /// <summary>Enable file consistency defaults.</summary>
    [Parameter]
    public bool FileConsistency { get; set; } = true;

    /// <summary>Required encoding for file consistency checks.</summary>
    [Parameter]
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;

    /// <summary>Required line endings for file consistency checks.</summary>
    [Parameter]
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;

    /// <summary>Directory names excluded from file consistency checks.</summary>
    [Parameter]
    public string[] FileConsistencyExcludeDirectories { get; set; } = new[] { "Build", "Docs", "Documentation", "Examples", "Tests" };

    /// <summary>Per-pattern encoding overrides for file consistency checks.</summary>
    [Parameter]
    public Hashtable? EncodingOverrides { get; set; }

    /// <summary>Enable cross-version PowerShell compatibility defaults.</summary>
    [Parameter]
    public bool Compatibility { get; set; } = true;

    /// <summary>Minimum percentage of cross-compatible files.</summary>
    [Parameter]
    public int MinimumCompatibilityPercentage { get; set; } = 95;

    /// <summary>Import the module under build before validation/test steps that need it.</summary>
    [Parameter]
    public bool ImportSelf { get; set; } = true;

    /// <summary>Import RequiredModules before validation/test steps that need them.</summary>
    [Parameter]
    public bool ImportRequiredModules { get; set; }

    /// <summary>Merge module source files during build.</summary>
    [Parameter]
    public bool MergeModuleOnBuild { get; set; } = true;

    /// <summary>Merge referenced functions from approved modules.</summary>
    [Parameter]
    public bool MergeFunctionsFromApprovedModules { get; set; }

    /// <summary>Enable signing for the built module.</summary>
    [Parameter]
    public bool SignModule { get; set; }

    /// <summary>Code-signing certificate thumbprint.</summary>
    [Parameter]
    public string? CertificateThumbprint { get; set; }

    /// <summary>Skip built-in placeholder replacements during build.</summary>
    [Parameter]
    public SwitchParameter SkipBuiltinReplacements { get; set; }

    /// <summary>Do not attempt to fix relative paths during merge.</summary>
    [Parameter]
    public SwitchParameter DoNotAttemptToFixRelativePaths { get; set; }

    /// <summary>Keep library-loading code dot-sourced.</summary>
    [Parameter]
    public SwitchParameter DotSourceLibraries { get; set; }

    /// <summary>Keep classes dot-sourced.</summary>
    [Parameter]
    public SwitchParameter DotSourceClasses { get; set; }

    /// <summary>Install missing module dependencies on the build host.</summary>
    [Parameter]
    public bool InstallMissingModules { get; set; } = true;

    /// <summary>Versioned install strategy.</summary>
    [Parameter]
    public InstallationStrategy VersionedInstallStrategy { get; set; } = InstallationStrategy.AutoRevision;

    /// <summary>Number of installed versions to keep.</summary>
    [Parameter]
    public int VersionedInstallKeep { get; set; } = 3;

    /// <summary>Kill locking processes before install.</summary>
    [Parameter]
    public SwitchParameter KillLockersBeforeInstall { get; set; }

    /// <summary>Force killing locking processes before install.</summary>
    [Parameter]
    public SwitchParameter KillLockersForce { get; set; }

    /// <summary>Path to the owned .NET project for binary-module builds.</summary>
    [Parameter]
    public string? NETProjectPath { get; set; }

    /// <summary>Project name for binary-module builds.</summary>
    [Parameter]
    public string? NETProjectName { get; set; }

    /// <summary>.NET build configuration.</summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string NETConfiguration { get; set; } = "Release";

    /// <summary>.NET target frameworks to build.</summary>
    [Parameter]
    public string[] NETFramework { get; set; } = Array.Empty<string>();

    /// <summary>Handle assemblies with the same name during import.</summary>
    [Parameter]
    public SwitchParameter NETHandleAssemblyWithSameName { get; set; }

    /// <summary>Load binary module dependencies through an AssemblyLoadContext on PowerShell Core.</summary>
    [Parameter]
    public SwitchParameter NETAssemblyLoadContext { get; set; }

    /// <summary>Resolve binary dependency conflicts.</summary>
    [Parameter]
    public SwitchParameter ResolveBinaryConflicts { get; set; }

    /// <summary>Project name used when resolving binary dependency conflicts.</summary>
    [Parameter]
    public string? ResolveBinaryConflictsName { get; set; }

    /// <summary>Controls type accelerator exposure for binary-module dependencies.</summary>
    [Parameter]
    public AssemblyTypeAcceleratorExportMode? NETAssemblyTypeAcceleratorMode { get; set; }

    /// <summary>Fully-qualified type names to expose as PowerShell type accelerators.</summary>
    [Parameter]
    public string[]? NETAssemblyTypeAccelerators { get; set; }

    /// <summary>Assembly names whose public types may be exposed as PowerShell type accelerators.</summary>
    [Parameter]
    public string[]? NETAssemblyTypeAcceleratorAssemblies { get; set; }

    /// <summary>When signing is enabled, include binary files.</summary>
    [Parameter]
    public SwitchParameter SignIncludeBinaries { get; set; }

    /// <summary>When signing is enabled, include internal scripts.</summary>
    [Parameter]
    public SwitchParameter SignIncludeInternals { get; set; }

    /// <summary>When signing is enabled, include executable files.</summary>
    [Parameter]
    public SwitchParameter SignIncludeExe { get; set; }

    /// <summary>Emits reusable build-profile configuration segments.</summary>
    protected override void ProcessRecord()
    {
        var bound = MyInvocation.BoundParameters;
        var request = new ModuleBuildProfileRequest
        {
            Profile = Profile,
            Documentation = Documentation,
            DocumentationPath = DocumentationPath,
            DocumentationReadmePath = DocumentationReadmePath,
            SyncExternalHelpToProjectRoot = SyncExternalHelpToProjectRoot,
            AboutTopicsSourcePath = AboutTopicsSourcePath ?? Array.Empty<string>(),
            Validation = Validation,
            EnableScriptAnalyzer = EnableScriptAnalyzer,
            FileConsistency = FileConsistency,
            RequiredEncoding = RequiredEncoding,
            RequiredLineEnding = RequiredLineEnding,
            FileConsistencyExcludeDirectories = FileConsistencyExcludeDirectories ?? Array.Empty<string>(),
            EncodingOverrides = EncodingOverrides,
            Compatibility = Compatibility,
            MinimumCompatibilityPercentage = MinimumCompatibilityPercentage,
            ImportSelf = ImportSelf,
            ImportRequiredModules = ImportRequiredModules,
            MergeModuleOnBuild = MergeModuleOnBuild,
            MergeFunctionsFromApprovedModulesSpecified = bound.ContainsKey(nameof(MergeFunctionsFromApprovedModules)),
            MergeFunctionsFromApprovedModules = MergeFunctionsFromApprovedModules,
            SignModule = SignModule,
            CertificateThumbprint = CertificateThumbprint,
            SkipBuiltinReplacements = SkipBuiltinReplacements.IsPresent,
            DoNotAttemptToFixRelativePaths = DoNotAttemptToFixRelativePaths.IsPresent,
            DotSourceLibraries = DotSourceLibraries.IsPresent,
            DotSourceClasses = DotSourceClasses.IsPresent,
            InstallMissingModules = InstallMissingModules,
            VersionedInstallStrategy = VersionedInstallStrategy,
            VersionedInstallKeep = VersionedInstallKeep,
            KillLockersBeforeInstall = KillLockersBeforeInstall.IsPresent,
            KillLockersForce = KillLockersForce.IsPresent,
            NETProjectPath = NETProjectPath,
            NETProjectName = NETProjectName,
            NETConfiguration = NETConfiguration,
            NETFramework = NETFramework ?? Array.Empty<string>(),
            NETHandleAssemblyWithSameName = NETHandleAssemblyWithSameName.IsPresent,
            NETAssemblyLoadContext = NETAssemblyLoadContext.IsPresent,
            ResolveBinaryConflicts = ResolveBinaryConflicts.IsPresent,
            ResolveBinaryConflictsName = ResolveBinaryConflictsName,
            NETAssemblyTypeAcceleratorMode = NETAssemblyTypeAcceleratorMode,
            NETAssemblyTypeAccelerators = NETAssemblyTypeAccelerators,
            NETAssemblyTypeAcceleratorAssemblies = NETAssemblyTypeAcceleratorAssemblies,
            SignIncludeBinariesSpecified = bound.ContainsKey(nameof(SignIncludeBinaries)),
            SignIncludeBinaries = SignIncludeBinaries.IsPresent,
            SignIncludeInternalsSpecified = bound.ContainsKey(nameof(SignIncludeInternals)),
            SignIncludeInternals = SignIncludeInternals.IsPresent,
            SignIncludeExeSpecified = bound.ContainsKey(nameof(SignIncludeExe)),
            SignIncludeExe = SignIncludeExe.IsPresent
        };

        try
        {
            foreach (var segment in new ModuleBuildProfileFactory().Create(request))
                WriteObject(segment);
        }
        catch (ArgumentException ex)
        {
            throw new PSArgumentException(ex.Message, ex);
        }
    }
}
