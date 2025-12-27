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
        var bound = MyInvocation.BoundParameters;

        BuildModuleConfiguration? buildModule = null;
        void EnsureBuildModule() => buildModule ??= new BuildModuleConfiguration();

        // BuildModule
        if (bound.ContainsKey(nameof(Enable))) { EnsureBuildModule(); buildModule!.Enable = Enable.IsPresent; }
        if (bound.ContainsKey(nameof(DeleteTargetModuleBeforeBuild))) { EnsureBuildModule(); buildModule!.DeleteBefore = DeleteTargetModuleBeforeBuild.IsPresent; }
        if (bound.ContainsKey(nameof(MergeModuleOnBuild))) { EnsureBuildModule(); buildModule!.Merge = MergeModuleOnBuild.IsPresent; }
        if (bound.ContainsKey(nameof(MergeFunctionsFromApprovedModules))) { EnsureBuildModule(); buildModule!.MergeMissing = MergeFunctionsFromApprovedModules.IsPresent; }
        if (bound.ContainsKey(nameof(SignModule))) { EnsureBuildModule(); buildModule!.SignMerged = SignModule.IsPresent; }
        if (bound.ContainsKey(nameof(DotSourceClasses))) { EnsureBuildModule(); buildModule!.ClassesDotSource = DotSourceClasses.IsPresent; }
        if (bound.ContainsKey(nameof(DotSourceLibraries))) { EnsureBuildModule(); buildModule!.LibraryDotSource = DotSourceLibraries.IsPresent; }
        if (bound.ContainsKey(nameof(SeparateFileLibraries))) { EnsureBuildModule(); buildModule!.LibrarySeparateFile = SeparateFileLibraries.IsPresent; }
        if (bound.ContainsKey(nameof(RefreshPSD1Only))) { EnsureBuildModule(); buildModule!.RefreshPSD1Only = RefreshPSD1Only.IsPresent; }
        if (bound.ContainsKey(nameof(UseWildcardForFunctions))) { EnsureBuildModule(); buildModule!.UseWildcardForFunctions = UseWildcardForFunctions.IsPresent; }
        if (bound.ContainsKey(nameof(LocalVersioning))) { EnsureBuildModule(); buildModule!.LocalVersion = LocalVersioning.IsPresent; }

        if (bound.ContainsKey(nameof(VersionedInstallStrategy))) { EnsureBuildModule(); buildModule!.VersionedInstallStrategy = VersionedInstallStrategy; }
        if (bound.ContainsKey(nameof(VersionedInstallKeep))) { EnsureBuildModule(); buildModule!.VersionedInstallKeep = VersionedInstallKeep; }

        if (bound.ContainsKey(nameof(DoNotAttemptToFixRelativePaths))) { EnsureBuildModule(); buildModule!.DoNotAttemptToFixRelativePaths = DoNotAttemptToFixRelativePaths.IsPresent; }
        if (bound.ContainsKey(nameof(NETMergeLibraryDebugging))) { EnsureBuildModule(); buildModule!.DebugDLL = NETMergeLibraryDebugging.IsPresent; }
        if (bound.ContainsKey(nameof(KillLockersBeforeInstall))) { EnsureBuildModule(); buildModule!.KillLockersBeforeInstall = KillLockersBeforeInstall.IsPresent; }
        if (bound.ContainsKey(nameof(KillLockersForce))) { EnsureBuildModule(); buildModule!.KillLockersForce = KillLockersForce.IsPresent; }
        if (bound.ContainsKey(nameof(AutoSwitchExactOnPublish))) { EnsureBuildModule(); buildModule!.AutoSwitchExactOnPublish = AutoSwitchExactOnPublish.IsPresent; }

        if (bound.ContainsKey(nameof(NETResolveBinaryConflictsName)))
        {
            EnsureBuildModule();
            buildModule!.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration { ProjectName = NETResolveBinaryConflictsName };
        }
        else if (bound.ContainsKey(nameof(NETResolveBinaryConflicts)))
        {
            EnsureBuildModule();
            buildModule!.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration { Enabled = NETResolveBinaryConflicts.IsPresent };
        }

        if (buildModule is not null)
            WriteObject(new ConfigurationBuildSegment { BuildModule = buildModule });

        // Signing options
        SigningOptionsConfiguration? signing = null;
        void EnsureSigning() => signing ??= new SigningOptionsConfiguration();

        if (bound.ContainsKey(nameof(SignIncludeInternals))) { EnsureSigning(); signing!.IncludeInternals = SignIncludeInternals.IsPresent; }
        if (bound.ContainsKey(nameof(SignIncludeBinaries))) { EnsureSigning(); signing!.IncludeBinaries = SignIncludeBinaries.IsPresent; }
        if (bound.ContainsKey(nameof(SignIncludeExe))) { EnsureSigning(); signing!.IncludeExe = SignIncludeExe.IsPresent; }
        if (bound.ContainsKey(nameof(SignCustomInclude))) { EnsureSigning(); signing!.Include = SignCustomInclude; }
        if (bound.ContainsKey(nameof(SignExcludePaths))) { EnsureSigning(); signing!.ExcludePaths = SignExcludePaths; }

        // Certificate selection (single branch)
        if (bound.ContainsKey(nameof(CertificateThumbprint)))
        {
            EnsureSigning();
            signing!.CertificateThumbprint = CertificateThumbprint;
        }
        else if (bound.ContainsKey(nameof(CertificatePFXPath)))
        {
            if (!bound.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXPath");

            EnsureSigning();
            signing!.CertificatePFXPath = CertificatePFXPath;
            signing.CertificatePFXPassword = CertificatePFXPassword;
        }
        else if (bound.ContainsKey(nameof(CertificatePFXBase64)))
        {
            if (!bound.ContainsKey(nameof(CertificatePFXPassword)))
                throw new PSArgumentException("CertificatePFXPassword is required when using CertificatePFXBase64");

            EnsureSigning();
            signing!.CertificatePFXBase64 = CertificatePFXBase64;
            signing.CertificatePFXPassword = CertificatePFXPassword;
        }

        if (signing is not null)
        {
            WriteObject(new ConfigurationOptionsSegment
            {
                Options = new ConfigurationOptions { Signing = signing }
            });
        }

        // BuildLibraries
        BuildLibrariesConfiguration? buildLibraries = null;
        bool enableBuildLibraries = false;
        void EnsureBuildLibraries() => buildLibraries ??= new BuildLibrariesConfiguration();

        if (bound.ContainsKey(nameof(NETConfiguration)))
        {
            EnsureBuildLibraries();
            buildLibraries!.Configuration = NETConfiguration;
            enableBuildLibraries = true;
        }
        if (bound.ContainsKey(nameof(NETFramework)))
        {
            EnsureBuildLibraries();
            buildLibraries!.Framework = NETFramework;
            enableBuildLibraries = true;
        }
        if (bound.ContainsKey(nameof(NETProjectName))) { EnsureBuildLibraries(); buildLibraries!.ProjectName = NETProjectName; }
        if (bound.ContainsKey(nameof(NETExcludeMainLibrary))) { EnsureBuildLibraries(); buildLibraries!.ExcludeMainLibrary = NETExcludeMainLibrary.IsPresent; }
        if (bound.ContainsKey(nameof(NETExcludeLibraryFilter))) { EnsureBuildLibraries(); buildLibraries!.ExcludeLibraryFilter = NETExcludeLibraryFilter; }
        if (bound.ContainsKey(nameof(NETIgnoreLibraryOnLoad))) { EnsureBuildLibraries(); buildLibraries!.IgnoreLibraryOnLoad = NETIgnoreLibraryOnLoad; }
        if (bound.ContainsKey(nameof(NETBinaryModule))) { EnsureBuildLibraries(); buildLibraries!.BinaryModule = NETBinaryModule; }
        if (bound.ContainsKey(nameof(NETHandleAssemblyWithSameName))) { EnsureBuildLibraries(); buildLibraries!.HandleAssemblyWithSameName = NETHandleAssemblyWithSameName.IsPresent; }
        if (bound.ContainsKey(nameof(NETLineByLineAddType))) { EnsureBuildLibraries(); buildLibraries!.NETLineByLineAddType = NETLineByLineAddType.IsPresent; }
        if (bound.ContainsKey(nameof(NETProjectPath))) { EnsureBuildLibraries(); buildLibraries!.NETProjectPath = NETProjectPath; }
        if (bound.ContainsKey(nameof(NETBinaryModuleCmdletScanDisabled))) { EnsureBuildLibraries(); buildLibraries!.BinaryModuleCmdletScanDisabled = NETBinaryModuleCmdletScanDisabled.IsPresent; }
        if (bound.ContainsKey(nameof(NETSearchClass))) { EnsureBuildLibraries(); buildLibraries!.SearchClass = NETSearchClass; }
        if (bound.ContainsKey(nameof(NETBinaryModuleDocumentation))) { EnsureBuildLibraries(); buildLibraries!.NETBinaryModuleDocumentation = NETBinaryModuleDocumentation.IsPresent; }
        if (bound.ContainsKey(nameof(NETHandleRuntimes))) { EnsureBuildLibraries(); buildLibraries!.HandleRuntimes = NETHandleRuntimes.IsPresent; }
        if (bound.ContainsKey(nameof(NETDoNotCopyLibrariesRecursively))) { EnsureBuildLibraries(); buildLibraries!.NETDoNotCopyLibrariesRecursively = NETDoNotCopyLibrariesRecursively.IsPresent; }

        if (buildLibraries is not null)
        {
            if (enableBuildLibraries) buildLibraries.Enable = true;
            WriteObject(new ConfigurationBuildLibrariesSegment { BuildLibraries = buildLibraries });
        }

        if (bound.ContainsKey(nameof(SkipBuiltinReplacements)) && SkipBuiltinReplacements.IsPresent)
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
}
