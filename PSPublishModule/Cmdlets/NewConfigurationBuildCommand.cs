using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Allows configuring the build process for a module.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits build configuration that is consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// It controls how the module is merged, signed, versioned, installed, and how optional .NET publishing is performed.
/// </para>
/// <para>
/// Dependency-related options in this cmdlet affect the build machine, not artefact packaging. Use
/// <c>InstallMissingModules</c> when the build host needs missing <c>RequiredModule</c> or
/// <c>ExternalModule</c> dependencies installed before merge/import/test steps run.
/// </para>
/// <para>
/// If you want dependencies copied into ZIP/unpacked artefacts, configure that separately with
/// <c>New-ConfigurationArtefact -AddRequiredModules</c>. Build-time installation and artefact packaging are designed
/// as separate decisions because many teams want one without the other.
/// </para>
/// <para>
/// For a broader dependency workflow explanation, see <c>about_ModuleDependencies</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Enable build and module merge, and keep a few installed versions</summary>
/// <code>New-ConfigurationBuild -Enable -MergeModuleOnBuild -LocalVersioning -VersionedInstallStrategy AutoRevision -VersionedInstallKeep 3</code>
/// </example>
/// <example>
/// <summary>Enable signing and terminate locking processes before install</summary>
/// <code>New-ConfigurationBuild -Enable -SignModule -CertificateThumbprint '0123456789ABCDEF' -KillLockersBeforeInstall -KillLockersForce</code>
/// </example>
/// <example>
/// <summary>Install missing dependencies from PSGallery before the build</summary>
/// <code>New-ConfigurationBuild -Enable -InstallMissingModules -InstallMissingModulesRepository 'PSGallery'</code>
/// <para>Use this when the build host does not already have the declared RequiredModule or ExternalModule dependencies installed.</para>
/// </example>
/// <example>
/// <summary>Resolve Auto or Latest online without installing first</summary>
/// <code>New-ConfigurationBuild -Enable -ResolveMissingModulesOnline -WarnIfRequiredModulesOutdated</code>
/// <para>Useful in CI or on clean machines when dependency versions should come from the repository rather than the local module cache.</para>
/// </example>
/// <example>
/// <summary>Install from a private repository with a token stored in a file</summary>
/// <code>New-ConfigurationBuild -Enable -InstallMissingModules -InstallMissingModulesRepository 'MyPrivateFeed' -InstallMissingModulesCredentialUserName 'build' -InstallMissingModulesCredentialSecretFilePath '.secrets\feed-token.txt'</code>
/// <para>Use the credential parameters only when the repository requires authentication.</para>
/// </example>
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

    /// <summary>
    /// When signing is enabled, binaries are signed by default (e.g., .dll, .cat).
    /// Use <c>-SignIncludeBinaries:$false</c> to opt out.
    /// </summary>
    [Parameter] public SwitchParameter SignIncludeBinaries { get; set; }

    /// <summary>When signing is enabled, include .exe files in signing.</summary>
    [Parameter] public SwitchParameter SignIncludeExe { get; set; }

    /// <summary>Override include patterns passed to the signer.</summary>
    [Parameter] public string[]? SignCustomInclude { get; set; }

    /// <summary>Additional path substrings to exclude from signing.</summary>
    [Parameter] public string[]? SignExcludePaths { get; set; }

    /// <summary>When signing is enabled, overwrite existing signatures (re-sign files).</summary>
    [Parameter] public SwitchParameter SignOverwriteSigned { get; set; }

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

    /// <summary>
    /// Synchronize the source .NET project version with the resolved module/manifest version before staging.
    /// This is opt-in and updates the source <c>.csproj</c> file when a project path can be resolved.
    /// </summary>
    [Parameter] public SwitchParameter SyncNETProjectVersion { get; set; }

    /// <summary>Controls how the module is installed into user Module roots after build.</summary>
    [Parameter]
    public InstallationStrategy? VersionedInstallStrategy { get; set; }

    /// <summary>How many versions to keep per module when using versioned installs.</summary>
    [Parameter] public int VersionedInstallKeep { get; set; }

    /// <summary>How to handle legacy flat module installs during install.</summary>
    [Parameter]
    public LegacyFlatModuleHandling? VersionedInstallLegacyFlatHandling { get; set; }

    /// <summary>Version folders to preserve during install pruning (for example older major versions).</summary>
    [Parameter]
    public string[]? VersionedInstallPreserveVersions { get; set; }

    /// <summary>
    /// Install missing module dependencies (<c>RequiredModule</c>/<c>ExternalModule</c>) before build. This affects
    /// the build host only; it does not bundle modules into artefacts.
    /// </summary>
    [Parameter] public SwitchParameter InstallMissingModules { get; set; }

    /// <summary>
    /// Force re-install or update even if dependencies are already installed. Useful when you want the build host to
    /// re-sync against the repository instead of accepting the current local state.
    /// </summary>
    [Parameter] public SwitchParameter InstallMissingModulesForce { get; set; }

    /// <summary>
    /// Allow prerelease versions when installing dependencies. Use this only when the dependency declaration and
    /// repository policy intentionally allow prerelease packages.
    /// </summary>
    [Parameter] public SwitchParameter InstallMissingModulesPrerelease { get; set; }

    /// <summary>
    /// Resolve Auto/Latest dependency versions from the repository without installing.
    /// When not explicitly set, this is auto-enabled if any RequiredModules use Auto/Latest/Guid Auto.
    /// </summary>
    [Parameter] public SwitchParameter ResolveMissingModulesOnline { get; set; }

    /// <summary>
    /// Warn if <c>RequiredModule</c> entries are older than the latest version available in the repository. This is a
    /// reporting hint and does not change the manifest or install anything by itself.
    /// </summary>
    [Parameter] public SwitchParameter WarnIfRequiredModulesOutdated { get; set; }

    /// <summary>
    /// Repository name used for dependency installation (defaults to <c>PSGallery</c>). Set this when your build
    /// should resolve dependencies from a named private feed or alternate gallery.
    /// </summary>
    [Parameter] public string? InstallMissingModulesRepository { get; set; }

    /// <summary>
    /// Credential user name for dependency installation. This is usually paired with
    /// <c>InstallMissingModulesCredentialSecret</c> or <c>InstallMissingModulesCredentialSecretFilePath</c>.
    /// </summary>
    [Parameter] public string? InstallMissingModulesCredentialUserName { get; set; }

    /// <summary>
    /// Credential secret or token for dependency installation. Prefer the file-path form in CI when you do not want
    /// the secret value embedded directly in scripts.
    /// </summary>
    [Parameter] public string? InstallMissingModulesCredentialSecret { get; set; }

    /// <summary>
    /// Path to a file containing the credential secret or token. This is often the safest option for automation and
    /// CI agents.
    /// </summary>
    [Parameter] public string? InstallMissingModulesCredentialSecretFilePath { get; set; }

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
        var request = new BuildConfigurationRequest
        {
            EnableSpecified = bound.ContainsKey(nameof(Enable)),
            Enable = Enable.IsPresent,
            DeleteTargetModuleBeforeBuildSpecified = bound.ContainsKey(nameof(DeleteTargetModuleBeforeBuild)),
            DeleteTargetModuleBeforeBuild = DeleteTargetModuleBeforeBuild.IsPresent,
            MergeModuleOnBuildSpecified = bound.ContainsKey(nameof(MergeModuleOnBuild)),
            MergeModuleOnBuild = MergeModuleOnBuild.IsPresent,
            MergeFunctionsFromApprovedModulesSpecified = bound.ContainsKey(nameof(MergeFunctionsFromApprovedModules)),
            MergeFunctionsFromApprovedModules = MergeFunctionsFromApprovedModules.IsPresent,
            SignModuleSpecified = bound.ContainsKey(nameof(SignModule)),
            SignModule = SignModule.IsPresent,
            DotSourceClassesSpecified = bound.ContainsKey(nameof(DotSourceClasses)),
            DotSourceClasses = DotSourceClasses.IsPresent,
            DotSourceLibrariesSpecified = bound.ContainsKey(nameof(DotSourceLibraries)),
            DotSourceLibraries = DotSourceLibraries.IsPresent,
            SeparateFileLibrariesSpecified = bound.ContainsKey(nameof(SeparateFileLibraries)),
            SeparateFileLibraries = SeparateFileLibraries.IsPresent,
            RefreshPSD1OnlySpecified = bound.ContainsKey(nameof(RefreshPSD1Only)),
            RefreshPSD1Only = RefreshPSD1Only.IsPresent,
            UseWildcardForFunctionsSpecified = bound.ContainsKey(nameof(UseWildcardForFunctions)),
            UseWildcardForFunctions = UseWildcardForFunctions.IsPresent,
            LocalVersioningSpecified = bound.ContainsKey(nameof(LocalVersioning)),
            LocalVersioning = LocalVersioning.IsPresent,
            SyncNETProjectVersionSpecified = bound.ContainsKey(nameof(SyncNETProjectVersion)),
            SyncNETProjectVersion = SyncNETProjectVersion.IsPresent,
            VersionedInstallStrategySpecified = bound.ContainsKey(nameof(VersionedInstallStrategy)),
            VersionedInstallStrategy = VersionedInstallStrategy,
            VersionedInstallKeepSpecified = bound.ContainsKey(nameof(VersionedInstallKeep)),
            VersionedInstallKeep = VersionedInstallKeep,
            VersionedInstallLegacyFlatHandlingSpecified = bound.ContainsKey(nameof(VersionedInstallLegacyFlatHandling)),
            VersionedInstallLegacyFlatHandling = VersionedInstallLegacyFlatHandling,
            VersionedInstallPreserveVersionsSpecified = bound.ContainsKey(nameof(VersionedInstallPreserveVersions)),
            VersionedInstallPreserveVersions = VersionedInstallPreserveVersions,
            InstallMissingModulesSpecified = bound.ContainsKey(nameof(InstallMissingModules)),
            InstallMissingModules = InstallMissingModules.IsPresent,
            InstallMissingModulesForceSpecified = bound.ContainsKey(nameof(InstallMissingModulesForce)),
            InstallMissingModulesForce = InstallMissingModulesForce.IsPresent,
            InstallMissingModulesPrereleaseSpecified = bound.ContainsKey(nameof(InstallMissingModulesPrerelease)),
            InstallMissingModulesPrerelease = InstallMissingModulesPrerelease.IsPresent,
            ResolveMissingModulesOnlineSpecified = bound.ContainsKey(nameof(ResolveMissingModulesOnline)),
            ResolveMissingModulesOnline = ResolveMissingModulesOnline.IsPresent,
            WarnIfRequiredModulesOutdatedSpecified = bound.ContainsKey(nameof(WarnIfRequiredModulesOutdated)),
            WarnIfRequiredModulesOutdated = WarnIfRequiredModulesOutdated.IsPresent,
            InstallMissingModulesRepositorySpecified = bound.ContainsKey(nameof(InstallMissingModulesRepository)),
            InstallMissingModulesRepository = InstallMissingModulesRepository,
            InstallMissingModulesCredentialUserNameSpecified = bound.ContainsKey(nameof(InstallMissingModulesCredentialUserName)),
            InstallMissingModulesCredentialUserName = InstallMissingModulesCredentialUserName,
            InstallMissingModulesCredentialSecretSpecified = bound.ContainsKey(nameof(InstallMissingModulesCredentialSecret)),
            InstallMissingModulesCredentialSecret = InstallMissingModulesCredentialSecret,
            InstallMissingModulesCredentialSecretFilePathSpecified = bound.ContainsKey(nameof(InstallMissingModulesCredentialSecretFilePath)),
            InstallMissingModulesCredentialSecretFilePath = InstallMissingModulesCredentialSecretFilePath,
            SkipBuiltinReplacementsSpecified = bound.ContainsKey(nameof(SkipBuiltinReplacements)),
            SkipBuiltinReplacements = SkipBuiltinReplacements.IsPresent,
            DoNotAttemptToFixRelativePathsSpecified = bound.ContainsKey(nameof(DoNotAttemptToFixRelativePaths)),
            DoNotAttemptToFixRelativePaths = DoNotAttemptToFixRelativePaths.IsPresent,
            CertificateThumbprintSpecified = bound.ContainsKey(nameof(CertificateThumbprint)),
            CertificateThumbprint = CertificateThumbprint,
            CertificatePFXPathSpecified = bound.ContainsKey(nameof(CertificatePFXPath)),
            CertificatePFXPath = CertificatePFXPath,
            CertificatePFXBase64Specified = bound.ContainsKey(nameof(CertificatePFXBase64)),
            CertificatePFXBase64 = CertificatePFXBase64,
            CertificatePFXPasswordSpecified = bound.ContainsKey(nameof(CertificatePFXPassword)),
            CertificatePFXPassword = CertificatePFXPassword,
            NETProjectPathSpecified = bound.ContainsKey(nameof(NETProjectPath)),
            NETProjectPath = NETProjectPath,
            NETConfigurationSpecified = bound.ContainsKey(nameof(NETConfiguration)),
            NETConfiguration = NETConfiguration,
            NETFrameworkSpecified = bound.ContainsKey(nameof(NETFramework)),
            NETFramework = NETFramework,
            NETProjectNameSpecified = bound.ContainsKey(nameof(NETProjectName)),
            NETProjectName = NETProjectName,
            NETExcludeMainLibrarySpecified = bound.ContainsKey(nameof(NETExcludeMainLibrary)),
            NETExcludeMainLibrary = NETExcludeMainLibrary.IsPresent,
            NETExcludeLibraryFilterSpecified = bound.ContainsKey(nameof(NETExcludeLibraryFilter)),
            NETExcludeLibraryFilter = NETExcludeLibraryFilter,
            NETIgnoreLibraryOnLoadSpecified = bound.ContainsKey(nameof(NETIgnoreLibraryOnLoad)),
            NETIgnoreLibraryOnLoad = NETIgnoreLibraryOnLoad,
            NETBinaryModuleSpecified = bound.ContainsKey(nameof(NETBinaryModule)),
            NETBinaryModule = NETBinaryModule,
            NETHandleAssemblyWithSameNameSpecified = bound.ContainsKey(nameof(NETHandleAssemblyWithSameName)),
            NETHandleAssemblyWithSameName = NETHandleAssemblyWithSameName.IsPresent,
            NETLineByLineAddTypeSpecified = bound.ContainsKey(nameof(NETLineByLineAddType)),
            NETLineByLineAddType = NETLineByLineAddType.IsPresent,
            NETBinaryModuleCmdletScanDisabledSpecified = bound.ContainsKey(nameof(NETBinaryModuleCmdletScanDisabled)),
            NETBinaryModuleCmdletScanDisabled = NETBinaryModuleCmdletScanDisabled.IsPresent,
            NETMergeLibraryDebuggingSpecified = bound.ContainsKey(nameof(NETMergeLibraryDebugging)),
            NETMergeLibraryDebugging = NETMergeLibraryDebugging.IsPresent,
            NETResolveBinaryConflictsSpecified = bound.ContainsKey(nameof(NETResolveBinaryConflicts)),
            NETResolveBinaryConflicts = NETResolveBinaryConflicts.IsPresent,
            NETResolveBinaryConflictsNameSpecified = bound.ContainsKey(nameof(NETResolveBinaryConflictsName)),
            NETResolveBinaryConflictsName = NETResolveBinaryConflictsName,
            NETBinaryModuleDocumentationSpecified = bound.ContainsKey(nameof(NETBinaryModuleDocumentation)),
            NETBinaryModuleDocumentation = NETBinaryModuleDocumentation.IsPresent,
            NETDoNotCopyLibrariesRecursivelySpecified = bound.ContainsKey(nameof(NETDoNotCopyLibrariesRecursively)),
            NETDoNotCopyLibrariesRecursively = NETDoNotCopyLibrariesRecursively.IsPresent,
            NETSearchClassSpecified = bound.ContainsKey(nameof(NETSearchClass)),
            NETSearchClass = NETSearchClass,
            NETHandleRuntimesSpecified = bound.ContainsKey(nameof(NETHandleRuntimes)),
            NETHandleRuntimes = NETHandleRuntimes.IsPresent,
            KillLockersBeforeInstallSpecified = bound.ContainsKey(nameof(KillLockersBeforeInstall)),
            KillLockersBeforeInstall = KillLockersBeforeInstall.IsPresent,
            KillLockersForceSpecified = bound.ContainsKey(nameof(KillLockersForce)),
            KillLockersForce = KillLockersForce.IsPresent,
            AutoSwitchExactOnPublishSpecified = bound.ContainsKey(nameof(AutoSwitchExactOnPublish)),
            AutoSwitchExactOnPublish = AutoSwitchExactOnPublish.IsPresent,
            SignIncludeInternalsSpecified = bound.ContainsKey(nameof(SignIncludeInternals)),
            SignIncludeInternals = SignIncludeInternals.IsPresent,
            SignIncludeBinariesSpecified = bound.ContainsKey(nameof(SignIncludeBinaries)),
            SignIncludeBinaries = SignIncludeBinaries.IsPresent,
            SignIncludeExeSpecified = bound.ContainsKey(nameof(SignIncludeExe)),
            SignIncludeExe = SignIncludeExe.IsPresent,
            SignCustomIncludeSpecified = bound.ContainsKey(nameof(SignCustomInclude)),
            SignCustomInclude = SignCustomInclude,
            SignExcludePathsSpecified = bound.ContainsKey(nameof(SignExcludePaths)),
            SignExcludePaths = SignExcludePaths,
            SignOverwriteSignedSpecified = bound.ContainsKey(nameof(SignOverwriteSigned)),
            SignOverwriteSigned = SignOverwriteSigned.IsPresent
        };

        try
        {
            foreach (var segment in new BuildConfigurationFactory().Create(request))
                WriteObject(segment);
        }
        catch (ArgumentException ex)
        {
            throw new PSArgumentException(ex.Message, ex);
        }
    }
}
