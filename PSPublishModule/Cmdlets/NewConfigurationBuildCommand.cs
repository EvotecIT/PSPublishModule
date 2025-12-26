using System;
using System.Management.Automation;
using PowerForge;

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
    public InstallationStrategy? VersionedInstallStrategy { get; set; }

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
            WriteObject(BuildModule(bm => bm.Enable = Enable.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(DeleteTargetModuleBeforeBuild)))
            WriteObject(BuildModule(bm => bm.DeleteBefore = DeleteTargetModuleBeforeBuild.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(MergeModuleOnBuild)))
            WriteObject(BuildModule(bm => bm.Merge = MergeModuleOnBuild.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(MergeFunctionsFromApprovedModules)))
            WriteObject(BuildModule(bm => bm.MergeMissing = MergeFunctionsFromApprovedModules.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SignModule)))
            WriteObject(BuildModule(bm => bm.SignMerged = SignModule.IsPresent));

        // Signing options
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeInternals)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeBinaries)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignIncludeExe)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignCustomInclude)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(SignExcludePaths)))
        {
            WriteObject(Signing(signing =>
            {
                signing.IncludeInternals = SignIncludeInternals.IsPresent;
                signing.IncludeBinaries = SignIncludeBinaries.IsPresent;
                signing.IncludeExe = SignIncludeExe.IsPresent;
                signing.Include = SignCustomInclude;
                signing.ExcludePaths = SignExcludePaths;
            }));
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DotSourceClasses)))
            WriteObject(BuildModule(bm => bm.ClassesDotSource = DotSourceClasses.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(DotSourceLibraries)))
            WriteObject(BuildModule(bm => bm.LibraryDotSource = DotSourceLibraries.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(SeparateFileLibraries)))
            WriteObject(BuildModule(bm => bm.LibrarySeparateFile = SeparateFileLibraries.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(RefreshPSD1Only)))
            WriteObject(BuildModule(bm => bm.RefreshPSD1Only = RefreshPSD1Only.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(UseWildcardForFunctions)))
            WriteObject(BuildModule(bm => bm.UseWildcardForFunctions = UseWildcardForFunctions.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(LocalVersioning)))
            WriteObject(BuildModule(bm => bm.LocalVersion = LocalVersioning.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallStrategy)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallKeep)))
        {
            WriteObject(BuildModule(bm =>
            {
                bm.VersionedInstallStrategy = VersionedInstallStrategy;
                bm.VersionedInstallKeep = MyInvocation.BoundParameters.ContainsKey(nameof(VersionedInstallKeep))
                    ? VersionedInstallKeep
                    : null;
            }));
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DoNotAttemptToFixRelativePaths)))
            WriteObject(BuildModule(bm => bm.DoNotAttemptToFixRelativePaths = DoNotAttemptToFixRelativePaths.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETMergeLibraryDebugging)))
            WriteObject(BuildModule(bm => bm.DebugDLL = NETMergeLibraryDebugging.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(KillLockersBeforeInstall)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(KillLockersForce)))
        {
            WriteObject(BuildModule(bm =>
            {
                bm.KillLockersBeforeInstall = KillLockersBeforeInstall.IsPresent;
                bm.KillLockersForce = KillLockersForce.IsPresent;
            }));
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(AutoSwitchExactOnPublish)))
            WriteObject(BuildModule(bm => bm.AutoSwitchExactOnPublish = AutoSwitchExactOnPublish.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETResolveBinaryConflictsName)))
        {
            WriteObject(BuildModule(bm => bm.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration
            {
                ProjectName = NETResolveBinaryConflictsName
            }));
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(NETResolveBinaryConflicts)))
        {
            WriteObject(BuildModule(bm => bm.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration
            {
                Enabled = NETResolveBinaryConflicts.IsPresent
            }));
        }

        // Certificate selection (single branch)
        if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificateThumbprint)))
        {
            WriteObject(Signing(signing => signing.CertificateThumbprint = CertificateThumbprint));
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPath)))
        {
            if (!MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXPath");

            WriteObject(Signing(signing =>
            {
                signing.CertificatePFXPath = CertificatePFXPath;
                signing.CertificatePFXPassword = CertificatePFXPassword;
            }));
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXBase64)))
        {
            if (!MyInvocation.BoundParameters.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXBase64");

            WriteObject(Signing(signing =>
            {
                signing.CertificatePFXBase64 = CertificatePFXBase64;
                signing.CertificatePFXPassword = CertificatePFXPassword;
            }));
        }

        // BuildLibraries
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETConfiguration)))
            WriteObject(BuildLibraries(bl => bl.Configuration = NETConfiguration, enable: true));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETFramework)))
            WriteObject(BuildLibraries(bl => bl.Framework = NETFramework, enable: true));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETProjectName)))
            WriteObject(BuildLibraries(bl => bl.ProjectName = NETProjectName));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETExcludeMainLibrary)))
            WriteObject(BuildLibraries(bl => bl.ExcludeMainLibrary = NETExcludeMainLibrary.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETExcludeLibraryFilter)))
            WriteObject(BuildLibraries(bl => bl.ExcludeLibraryFilter = NETExcludeLibraryFilter));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETIgnoreLibraryOnLoad)))
            WriteObject(BuildLibraries(bl => bl.IgnoreLibraryOnLoad = NETIgnoreLibraryOnLoad));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModule)))
            WriteObject(BuildLibraries(bl => bl.BinaryModule = NETBinaryModule));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETHandleAssemblyWithSameName)))
            WriteObject(BuildLibraries(bl => bl.HandleAssemblyWithSameName = NETHandleAssemblyWithSameName.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETLineByLineAddType)))
            WriteObject(BuildLibraries(bl => bl.NETLineByLineAddType = NETLineByLineAddType.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETProjectPath)))
            WriteObject(BuildLibraries(bl => bl.NETProjectPath = NETProjectPath));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModuleCmdletScanDisabled)))
            WriteObject(BuildLibraries(bl => bl.BinaryModuleCmdletScanDisabled = NETBinaryModuleCmdletScanDisabled.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETSearchClass)))
            WriteObject(BuildLibraries(bl => bl.SearchClass = NETSearchClass));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETBinaryModuleDocumentation)))
            WriteObject(BuildLibraries(bl => bl.NETBinaryModuleDocumentation = NETBinaryModuleDocumentation.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETHandleRuntimes)))
            WriteObject(BuildLibraries(bl => bl.HandleRuntimes = NETHandleRuntimes.IsPresent));
        if (MyInvocation.BoundParameters.ContainsKey(nameof(NETDoNotCopyLibrariesRecursively)))
            WriteObject(BuildLibraries(bl => bl.NETDoNotCopyLibrariesRecursively = NETDoNotCopyLibrariesRecursively.IsPresent));

        if (MyInvocation.BoundParameters.ContainsKey(nameof(SkipBuiltinReplacements)))
        {
            WriteObject(new ConfigurationPlaceHolderOptionSegment
            {
                PlaceHolderOption = new PlaceHolderOptionConfiguration
                {
                    SkipBuiltinReplacements = true
                }
            });
        }
    }

    private static ConfigurationBuildSegment BuildModule(Action<BuildModuleConfiguration> apply)
    {
        var bm = new BuildModuleConfiguration();
        apply(bm);
        return new ConfigurationBuildSegment { BuildModule = bm };
    }

    private static ConfigurationOptionsSegment Signing(Action<SigningOptionsConfiguration> apply)
    {
        var signing = new SigningOptionsConfiguration();
        apply(signing);
        return new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Signing = signing } };
    }

    private static ConfigurationBuildLibrariesSegment BuildLibraries(Action<BuildLibrariesConfiguration> apply, bool enable = false)
    {
        var bl = new BuildLibrariesConfiguration();
        apply(bl);
        if (enable) bl.Enable = true;
        return new ConfigurationBuildLibrariesSegment { BuildLibraries = bl };
    }
}
