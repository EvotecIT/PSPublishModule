using System;
using System.Collections;
using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Allows configuring the build process for a module.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationBuild")]
public sealed class NewConfigurationBuildCommand : PSCmdlet
{
    /// <summary>Enable build process.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Delete target module before build.</summary>
    [Parameter] public SwitchParameter DeleteTargetModuleBeforeBuild { get; set; }

    /// <summary>Merge module on build (combine Private/Public/Classes/Enums into one PSM1).</summary>
    [Parameter] public SwitchParameter MergeModuleOnBuild { get; set; }

    /// <summary>When merging, also include functions from ApprovedModules referenced by the module.</summary>
    [Parameter] public SwitchParameter MergeFunctionsFromApprovedModules { get; set; }

    /// <summary>Enable code-signing for the built module output.</summary>
    [Parameter] public SwitchParameter SignModule { get; set; }

    /// <summary>When signing is enabled, also sign scripts that reside under the Internals folder.</summary>
    [Parameter] public SwitchParameter SignIncludeInternals { get; set; }

    /// <summary>When signing is enabled, include binary files (e.g., .dll, .cat) in signing.</summary>
    [Parameter] public SwitchParameter SignIncludeBinaries { get; set; }

    /// <summary>When signing is enabled, include .exe files in signing.</summary>
    [Parameter] public SwitchParameter SignIncludeExe { get; set; }

    /// <summary>Override include patterns passed to the signer.</summary>
    [Parameter] public string[]? SignCustomInclude { get; set; }

    /// <summary>Additional path substrings to exclude from signing.</summary>
    [Parameter] public string[]? SignExcludePaths { get; set; }

    /// <summary>Keep classes in a separate dot-sourced file instead of merging into the main PSM1.</summary>
    [Parameter] public SwitchParameter DotSourceClasses { get; set; }

    /// <summary>Keep library-loading code in a separate dot-sourced file.</summary>
    [Parameter] public SwitchParameter DotSourceLibraries { get; set; }

    /// <summary>Write library-loading code into a distinct file and reference it via ScriptsToProcess/DotSource.</summary>
    [Parameter] public SwitchParameter SeparateFileLibraries { get; set; }

    /// <summary>Only regenerate the manifest (PSD1) without rebuilding/merging other artefacts.</summary>
    [Parameter] public SwitchParameter RefreshPSD1Only { get; set; }

    /// <summary>Export all functions/aliases via wildcard in PSD1.</summary>
    [Parameter] public SwitchParameter UseWildcardForFunctions { get; set; }

    /// <summary>Use local versioning (bump PSD1 version on each build without querying PSGallery).</summary>
    [Parameter] public SwitchParameter LocalVersioning { get; set; }

    /// <summary>Controls how the module is installed into user Module roots after build.</summary>
    [Parameter]
    [ValidateSet("Exact", "AutoRevision")]
    public string? VersionedInstallStrategy { get; set; }

    /// <summary>How many versions to keep per module when using versioned installs.</summary>
    [Parameter] public int VersionedInstallKeep { get; set; }

    /// <summary>Disables built-in replacements done by the module builder.</summary>
    [Parameter] public SwitchParameter SkipBuiltinReplacements { get; set; }

    /// <summary>Do not attempt to fix relative paths during merge.</summary>
    [Parameter] public SwitchParameter DoNotAttemptToFixRelativePaths { get; set; }

    /// <summary>Thumbprint of a code-signing certificate from the local cert store.</summary>
    [Parameter] public string? CertificateThumbprint { get; set; }

    /// <summary>Path to a PFX containing a code-signing certificate.</summary>
    [Parameter] public string? CertificatePFXPath { get; set; }

    /// <summary>Base64 string of a PFX containing a code-signing certificate.</summary>
    [Parameter] public string? CertificatePFXBase64 { get; set; }

    /// <summary>Password for the PFX provided via CertificatePFXPath or CertificatePFXBase64.</summary>
    [Parameter] public string? CertificatePFXPassword { get; set; }

    /// <summary>Path to the .NET project to build (useful when not in Sources folder).</summary>
    [Parameter] public string? NETProjectPath { get; set; }

    /// <summary>Build configuration for .NET projects (Release or Debug).</summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string? NETConfiguration { get; set; }

    /// <summary>Target frameworks for .NET build.</summary>
    [Parameter] public string[]? NETFramework { get; set; }

    /// <summary>Project name for the .NET project (required when NETProjectPath is provided).</summary>
    [Parameter] public string? NETProjectName { get; set; }

    /// <summary>Exclude main library from build output.</summary>
    [Parameter] public SwitchParameter NETExcludeMainLibrary { get; set; }

    /// <summary>Filters for libraries that should be excluded from build output.</summary>
    [Parameter] public string[]? NETExcludeLibraryFilter { get; set; }

    /// <summary>Exclude libraries from being loaded by PSM1/Libraries.ps1.</summary>
    [Parameter] public string[]? NETIgnoreLibraryOnLoad { get; set; }

    /// <summary>Binary module names (DLL file names) to import in the module.</summary>
    [Parameter] public string[]? NETBinaryModule { get; set; }

    /// <summary>Handle 'assembly with same name is already loaded' by wrapping Add-Type logic.</summary>
    [Parameter]
    [Alias("HandleAssemblyWithSameName")]
    public SwitchParameter NETHandleAssemblyWithSameName { get; set; }

    /// <summary>Add-Type libraries line by line (legacy debugging option).</summary>
    [Parameter] public SwitchParameter NETLineByLineAddType { get; set; }

    /// <summary>Disable cmdlet scanning for the binary module.</summary>
    [Parameter] public SwitchParameter NETBinaryModuleCmdletScanDisabled { get; set; }

    /// <summary>Debug DLL merge (legacy setting).</summary>
    [Parameter]
    [Alias("MergeLibraryDebugging")]
    public SwitchParameter NETMergeLibraryDebugging { get; set; }

    /// <summary>Enable resolving binary conflicts.</summary>
    [Parameter]
    [Alias("ResolveBinaryConflicts")]
    public SwitchParameter NETResolveBinaryConflicts { get; set; }

    /// <summary>Project name used when resolving binary conflicts.</summary>
    [Parameter]
    [Alias("ResolveBinaryConflictsName")]
    public string? NETResolveBinaryConflictsName { get; set; }

    /// <summary>Enable binary module documentation.</summary>
    [Parameter]
    [Alias("NETDocumentation", "NETBinaryModuleDocumenation")]
    public SwitchParameter NETBinaryModuleDocumentation { get; set; }

    /// <summary>Do not copy libraries recursively (legacy option).</summary>
    [Parameter] public SwitchParameter NETDoNotCopyLibrariesRecursively { get; set; }

    /// <summary>Search class (legacy option).</summary>
    [Parameter] public string? NETSearchClass { get; set; }

    /// <summary>Handle runtimes folder when copying libraries.</summary>
    [Parameter] public SwitchParameter NETHandleRuntimes { get; set; }

    /// <summary>Kill locking processes before install.</summary>
    [Parameter] public SwitchParameter KillLockersBeforeInstall { get; set; }

    /// <summary>Force killing locking processes before install.</summary>
    [Parameter] public SwitchParameter KillLockersForce { get; set; }

    /// <summary>Auto switch VersionedInstallStrategy to Exact when publishing.</summary>
    [Parameter] public SwitchParameter AutoSwitchExactOnPublish { get; set; }

    /// <summary>Emits one or more configuration objects for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        // BuildModule
        if (MyInvocation.BoundParameters.ContainsKey(nameof(Enable)))
            WriteObject(BuildModule("Enable", Enable.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(DeleteTargetModuleBeforeBuild)))
            WriteObject(BuildModule("DeleteBefore", DeleteTargetModuleBeforeBuild.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(MergeModuleOnBuild)))
            WriteObject(BuildModule("Merge", MergeModuleOnBuild.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(MergeFunctionsFromApprovedModules)))
            WriteObject(BuildModule("MergeMissing", MergeFunctionsFromApprovedModules.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SignModule)))
            WriteObject(BuildModule("SignMerged", SignModule.IsPresent));

        // Signing options
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeInternals)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeBinaries)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeExe)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignCustomInclude)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignExcludePaths)))
        {
            var signing = new OrderedDictionary
            {
                ["IncludeInternals"] = SignIncludeInternals.IsPresent,
                ["IncludeBinaries"] = SignIncludeBinaries.IsPresent,
                ["IncludeExe"] = SignIncludeExe.IsPresent,
                ["Include"] = SignCustomInclude,
                ["ExcludePaths"] = SignExcludePaths
            };

            WriteObject(Options("Signing", signing));
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DotSourceClasses)))
            WriteObject(BuildModule("ClassesDotSource", DotSourceClasses.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(DotSourceLibraries)))
            WriteObject(BuildModule("LibraryDotSource", DotSourceLibraries.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SeparateFileLibraries)))
            WriteObject(BuildModule("LibrarySeparateFile", SeparateFileLibraries.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(RefreshPSD1Only)))
            WriteObject(BuildModule("RefreshPSD1Only", RefreshPSD1Only.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(UseWildcardForFunctions)))
            WriteObject(BuildModule("UseWildcardForFunctions", UseWildcardForFunctions.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(LocalVersioning)))
            WriteObject(BuildModule("LocalVersion", LocalVersioning.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallStrategy)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallKeep)))
        {
            var bm = new OrderedDictionary
            {
                ["VersionedInstallStrategy"] = VersionedInstallStrategy,
                ["VersionedInstallKeep"] = MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallKeep))
                    ? (object)VersionedInstallKeep
                    : null
            };
            WriteObject(new OrderedDictionary { ["Type"] = "Build", ["BuildModule"] = bm });
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DoNotAttemptToFixRelativePaths)))
            WriteObject(BuildModule("DoNotAttemptToFixRelativePaths", DoNotAttemptToFixRelativePaths.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETMergeLibraryDebugging)))
            WriteObject(BuildModule("DebugDLL", NETMergeLibraryDebugging.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(KillLockersBeforeInstall)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(KillLockersForce)))
        {
            var bm = new OrderedDictionary
            {
                ["KillLockersBeforeInstall"] = KillLockersBeforeInstall.IsPresent,
                ["KillLockersForce"] = KillLockersForce.IsPresent
            };
            WriteObject(new OrderedDictionary { ["Type"] = "Build", ["BuildModule"] = bm });
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(AutoSwitchExactOnPublish)))
            WriteObject(BuildModule("AutoSwitchExactOnPublish", AutoSwitchExactOnPublish.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETResolveBinaryConflictsName)))
        {
            var bm = new OrderedDictionary
            {
                ["ResolveBinaryConflicts"] = new Hashtable { ["ProjectName"] = NETResolveBinaryConflictsName }
            };
            WriteObject(new OrderedDictionary { ["Type"] = "Build", ["BuildModule"] = bm });
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(NETResolveBinaryConflicts)))
        {
            WriteObject(BuildModule("ResolveBinaryConflicts", NETResolveBinaryConflicts.IsPresent));
        }

        // Certificate selection (single branch)
        if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificateThumbprint)))
        {
            WriteObject(Options("Signing", new OrderedDictionary { ["CertificateThumbprint"] = CertificateThumbprint }));
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPath)))
        {
            if (!MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXPath");

            WriteObject(Options("Signing", new OrderedDictionary
            {
                ["CertificatePFXPath"] = CertificatePFXPath,
                ["CertificatePFXPassword"] = CertificatePFXPassword
            }));
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXBase64)))
        {
            if (!MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXBase64");

            WriteObject(Options("Signing", new OrderedDictionary
            {
                ["CertificatePFXBase64"] = CertificatePFXBase64,
                ["CertificatePFXPassword"] = CertificatePFXPassword
            }));
        }

        // BuildLibraries
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETConfiguration)))
            WriteObject(BuildLibraries("Configuration", NETConfiguration, enable: true));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETFramework)))
            WriteObject(BuildLibraries("Framework", NETFramework, enable: true));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETProjectName)))
            WriteObject(BuildLibraries("ProjectName", NETProjectName));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETExcludeMainLibrary)))
            WriteObject(BuildLibraries("ExcludeMainLibrary", NETExcludeMainLibrary.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETExcludeLibraryFilter)))
            WriteObject(BuildLibraries("ExcludeLibraryFilter", NETExcludeLibraryFilter));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETIgnoreLibraryOnLoad)))
            WriteObject(BuildLibraries("IgnoreLibraryOnLoad", NETIgnoreLibraryOnLoad));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModule)))
            WriteObject(BuildLibraries("BinaryModule", NETBinaryModule));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETHandleAssemblyWithSameName)))
            WriteObject(BuildLibraries("HandleAssemblyWithSameName", NETHandleAssemblyWithSameName.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETLineByLineAddType)))
            WriteObject(BuildLibraries("NETLineByLineAddType", NETLineByLineAddType.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETProjectPath)))
            WriteObject(BuildLibraries("NETProjectPath", NETProjectPath));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModuleCmdletScanDisabled)))
            WriteObject(BuildLibraries("BinaryModuleCmdletScanDisabled", NETBinaryModuleCmdletScanDisabled.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETSearchClass)))
            WriteObject(BuildLibraries("SearchClass", NETSearchClass));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModuleDocumentation)))
            WriteObject(BuildLibraries("NETBinaryModuleDocumentation", NETBinaryModuleDocumentation.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETHandleRuntimes)))
            WriteObject(BuildLibraries("HandleRuntimes", NETHandleRuntimes.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETDoNotCopyLibrariesRecursively)))
            WriteObject(BuildLibraries("NETDoNotCopyLibrariesRecursively", NETDoNotCopyLibrariesRecursively.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(SkipBuiltinReplacements)))
        {
            var option = new OrderedDictionary
            {
                ["Type"] = "PlaceHolderOption",
                ["PlaceHolderOption"] = new OrderedDictionary
                {
                    ["SkipBuiltinReplacements"] = true
                }
            };
            WriteObject(option);
        }
    }

    private static OrderedDictionary BuildModule(string key, object? value)
    {
        return new OrderedDictionary
        {
            ["Type"] = "Build",
            ["BuildModule"] = new OrderedDictionary { [key] = value }
        };
    }

    private static OrderedDictionary Options(string optionsKey, OrderedDictionary optionsValue)
    {
        return new OrderedDictionary
        {
            ["Type"] = "Options",
            ["Options"] = new OrderedDictionary
            {
                [optionsKey] = optionsValue
            }
        };
    }

    private static OrderedDictionary BuildLibraries(string key, object? value, bool enable = false)
    {
        var inner = new OrderedDictionary { [key] = value };
        if (enable) inner["Enable"] = true;

        return new OrderedDictionary
        {
            ["Type"] = "BuildLibraries",
            ["BuildLibraries"] = inner
        };
    }
}
