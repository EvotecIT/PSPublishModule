using System.IO;

namespace PowerForge;

internal sealed class BuildConfigurationFactory
{
    public IReadOnlyList<IConfigurationSegment> Create(BuildConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var segments = new List<IConfigurationSegment>();

        var buildModule = CreateBuildModuleSegment(request);
        if (buildModule is not null)
            segments.Add(buildModule);

        var signing = CreateSigningSegment(request);
        if (signing is not null)
            segments.Add(signing);

        var buildLibraries = CreateBuildLibrariesSegment(request);
        if (buildLibraries is not null)
            segments.Add(buildLibraries);

        var placeholder = CreatePlaceHolderOptionsSegment(request);
        if (placeholder is not null)
            segments.Add(placeholder);

        return segments;
    }

    private static ConfigurationBuildSegment? CreateBuildModuleSegment(BuildConfigurationRequest request)
    {
        BuildModuleConfiguration? buildModule = null;
        void EnsureBuildModule() => buildModule ??= new BuildModuleConfiguration();

        if (request.EnableSpecified) { EnsureBuildModule(); buildModule!.Enable = request.Enable; }
        if (request.DeleteTargetModuleBeforeBuildSpecified) { EnsureBuildModule(); buildModule!.DeleteBefore = request.DeleteTargetModuleBeforeBuild; }
        if (request.MergeModuleOnBuildSpecified) { EnsureBuildModule(); buildModule!.Merge = request.MergeModuleOnBuild; }
        if (request.MergeFunctionsFromApprovedModulesSpecified) { EnsureBuildModule(); buildModule!.MergeMissing = request.MergeFunctionsFromApprovedModules; }
        if (request.SignModuleSpecified) { EnsureBuildModule(); buildModule!.SignMerged = request.SignModule; }
        if (request.DotSourceClassesSpecified) { EnsureBuildModule(); buildModule!.ClassesDotSource = request.DotSourceClasses; }
        if (request.DotSourceLibrariesSpecified) { EnsureBuildModule(); buildModule!.LibraryDotSource = request.DotSourceLibraries; }
        if (request.SeparateFileLibrariesSpecified) { EnsureBuildModule(); buildModule!.LibrarySeparateFile = request.SeparateFileLibraries; }
        if (request.RefreshPSD1OnlySpecified) { EnsureBuildModule(); buildModule!.RefreshPSD1Only = request.RefreshPSD1Only; }
        if (request.UseWildcardForFunctionsSpecified) { EnsureBuildModule(); buildModule!.UseWildcardForFunctions = request.UseWildcardForFunctions; }
        if (request.LocalVersioningSpecified) { EnsureBuildModule(); buildModule!.LocalVersion = request.LocalVersioning; }
        if (request.SyncNETProjectVersionSpecified) { EnsureBuildModule(); buildModule!.SyncNETProjectVersion = request.SyncNETProjectVersion; }
        if (request.VersionedInstallStrategySpecified) { EnsureBuildModule(); buildModule!.VersionedInstallStrategy = request.VersionedInstallStrategy; }
        if (request.VersionedInstallKeepSpecified) { EnsureBuildModule(); buildModule!.VersionedInstallKeep = request.VersionedInstallKeep; }
        if (request.VersionedInstallLegacyFlatHandlingSpecified) { EnsureBuildModule(); buildModule!.LegacyFlatHandling = request.VersionedInstallLegacyFlatHandling; }
        if (request.VersionedInstallPreserveVersionsSpecified) { EnsureBuildModule(); buildModule!.PreserveInstallVersions = request.VersionedInstallPreserveVersions; }
        if (request.InstallMissingModulesSpecified) { EnsureBuildModule(); buildModule!.InstallMissingModules = request.InstallMissingModules; }
        if (request.InstallMissingModulesForceSpecified) { EnsureBuildModule(); buildModule!.InstallMissingModulesForce = request.InstallMissingModulesForce; }
        if (request.InstallMissingModulesPrereleaseSpecified) { EnsureBuildModule(); buildModule!.InstallMissingModulesPrerelease = request.InstallMissingModulesPrerelease; }
        if (request.ResolveMissingModulesOnlineSpecified) { EnsureBuildModule(); buildModule!.ResolveMissingModulesOnline = request.ResolveMissingModulesOnline; }
        if (request.WarnIfRequiredModulesOutdatedSpecified) { EnsureBuildModule(); buildModule!.WarnIfRequiredModulesOutdated = request.WarnIfRequiredModulesOutdated; }
        if (request.InstallMissingModulesRepositorySpecified) { EnsureBuildModule(); buildModule!.InstallMissingModulesRepository = request.InstallMissingModulesRepository; }

        var missingModulesSecret = ResolveSecret(
            request.InstallMissingModulesCredentialSecretSpecified,
            request.InstallMissingModulesCredentialSecret,
            request.InstallMissingModulesCredentialSecretFilePathSpecified,
            request.InstallMissingModulesCredentialSecretFilePath);

        if (!string.IsNullOrWhiteSpace(missingModulesSecret) &&
            string.IsNullOrWhiteSpace(request.InstallMissingModulesCredentialUserName))
        {
            throw new ArgumentException("InstallMissingModulesCredentialUserName is required when InstallMissingModulesCredentialSecret/InstallMissingModulesCredentialSecretFilePath is provided.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(missingModulesSecret) &&
            !string.IsNullOrWhiteSpace(request.InstallMissingModulesCredentialUserName))
        {
            EnsureBuildModule();
            buildModule!.InstallMissingModulesCredential = new RepositoryCredential
            {
                UserName = request.InstallMissingModulesCredentialUserName!.Trim(),
                Secret = missingModulesSecret
            };
        }

        if (request.DoNotAttemptToFixRelativePathsSpecified) { EnsureBuildModule(); buildModule!.DoNotAttemptToFixRelativePaths = request.DoNotAttemptToFixRelativePaths; }
        if (request.NETMergeLibraryDebuggingSpecified) { EnsureBuildModule(); buildModule!.DebugDLL = request.NETMergeLibraryDebugging; }
        if (request.KillLockersBeforeInstallSpecified) { EnsureBuildModule(); buildModule!.KillLockersBeforeInstall = request.KillLockersBeforeInstall; }
        if (request.KillLockersForceSpecified) { EnsureBuildModule(); buildModule!.KillLockersForce = request.KillLockersForce; }
        if (request.AutoSwitchExactOnPublishSpecified) { EnsureBuildModule(); buildModule!.AutoSwitchExactOnPublish = request.AutoSwitchExactOnPublish; }

        if (request.NETResolveBinaryConflictsNameSpecified)
        {
            EnsureBuildModule();
            buildModule!.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration { ProjectName = request.NETResolveBinaryConflictsName };
        }
        else if (request.NETResolveBinaryConflictsSpecified)
        {
            EnsureBuildModule();
            buildModule!.ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration { Enabled = request.NETResolveBinaryConflicts };
        }

        return buildModule is null ? null : new ConfigurationBuildSegment { BuildModule = buildModule };
    }

    private static ConfigurationOptionsSegment? CreateSigningSegment(BuildConfigurationRequest request)
    {
        SigningOptionsConfiguration? signing = null;
        void EnsureSigning() => signing ??= new SigningOptionsConfiguration();

        if (request.SignIncludeInternalsSpecified) { EnsureSigning(); signing!.IncludeInternals = request.SignIncludeInternals; }
        if (request.SignIncludeBinariesSpecified) { EnsureSigning(); signing!.IncludeBinaries = request.SignIncludeBinaries; }
        if (request.SignIncludeExeSpecified) { EnsureSigning(); signing!.IncludeExe = request.SignIncludeExe; }
        if (request.SignCustomIncludeSpecified) { EnsureSigning(); signing!.Include = request.SignCustomInclude; }
        if (request.SignExcludePathsSpecified) { EnsureSigning(); signing!.ExcludePaths = request.SignExcludePaths; }
        if (request.SignOverwriteSignedSpecified) { EnsureSigning(); signing!.OverwriteSigned = request.SignOverwriteSigned; }

        if (request.CertificateThumbprintSpecified)
        {
            EnsureSigning();
            signing!.CertificateThumbprint = request.CertificateThumbprint;
        }
        else if (request.CertificatePFXPathSpecified)
        {
            if (!request.CertificatePFXPasswordSpecified)
                throw new ArgumentException("CertificatePFXPassword is required when using CertificatePFXPath", nameof(request));

            EnsureSigning();
            signing!.CertificatePFXPath = request.CertificatePFXPath;
            signing.CertificatePFXPassword = request.CertificatePFXPassword;
        }
        else if (request.CertificatePFXBase64Specified)
        {
            if (!request.CertificatePFXPasswordSpecified)
                throw new ArgumentException("CertificatePFXPassword is required when using CertificatePFXBase64", nameof(request));

            EnsureSigning();
            signing!.CertificatePFXBase64 = request.CertificatePFXBase64;
            signing.CertificatePFXPassword = request.CertificatePFXPassword;
        }

        return signing is null
            ? null
            : new ConfigurationOptionsSegment
            {
                Options = new ConfigurationOptions { Signing = signing }
            };
    }

    private static ConfigurationBuildLibrariesSegment? CreateBuildLibrariesSegment(BuildConfigurationRequest request)
    {
        BuildLibrariesConfiguration? buildLibraries = null;
        var enableBuildLibraries = false;
        void EnsureBuildLibraries() => buildLibraries ??= new BuildLibrariesConfiguration();

        if (request.NETConfigurationSpecified)
        {
            EnsureBuildLibraries();
            buildLibraries!.Configuration = request.NETConfiguration;
            enableBuildLibraries = true;
        }

        if (request.NETFrameworkSpecified)
        {
            EnsureBuildLibraries();
            buildLibraries!.Framework = request.NETFramework;
            enableBuildLibraries = true;
        }

        if (request.NETProjectNameSpecified) { EnsureBuildLibraries(); buildLibraries!.ProjectName = request.NETProjectName; }
        if (request.NETExcludeMainLibrarySpecified) { EnsureBuildLibraries(); buildLibraries!.ExcludeMainLibrary = request.NETExcludeMainLibrary; }
        if (request.NETExcludeLibraryFilterSpecified) { EnsureBuildLibraries(); buildLibraries!.ExcludeLibraryFilter = request.NETExcludeLibraryFilter; }
        if (request.NETIgnoreLibraryOnLoadSpecified) { EnsureBuildLibraries(); buildLibraries!.IgnoreLibraryOnLoad = request.NETIgnoreLibraryOnLoad; }
        if (request.NETBinaryModuleSpecified) { EnsureBuildLibraries(); buildLibraries!.BinaryModule = request.NETBinaryModule; }
        if (request.NETHandleAssemblyWithSameNameSpecified) { EnsureBuildLibraries(); buildLibraries!.HandleAssemblyWithSameName = request.NETHandleAssemblyWithSameName; }
        if (request.NETLineByLineAddTypeSpecified) { EnsureBuildLibraries(); buildLibraries!.NETLineByLineAddType = request.NETLineByLineAddType; }
        if (request.NETProjectPathSpecified) { EnsureBuildLibraries(); buildLibraries!.NETProjectPath = request.NETProjectPath; }
        if (request.NETBinaryModuleCmdletScanDisabledSpecified) { EnsureBuildLibraries(); buildLibraries!.BinaryModuleCmdletScanDisabled = request.NETBinaryModuleCmdletScanDisabled; }
        if (request.NETSearchClassSpecified) { EnsureBuildLibraries(); buildLibraries!.SearchClass = request.NETSearchClass; }
        if (request.NETBinaryModuleDocumentationSpecified) { EnsureBuildLibraries(); buildLibraries!.NETBinaryModuleDocumentation = request.NETBinaryModuleDocumentation; }
        if (request.NETHandleRuntimesSpecified) { EnsureBuildLibraries(); buildLibraries!.HandleRuntimes = request.NETHandleRuntimes; }
        if (request.NETAssemblyLoadContextSpecified) { EnsureBuildLibraries(); buildLibraries!.UseAssemblyLoadContext = request.NETAssemblyLoadContext; }
        if (request.NETAssemblyTypeAcceleratorModeSpecified) { EnsureBuildLibraries(); buildLibraries!.AssemblyTypeAcceleratorMode = request.NETAssemblyTypeAcceleratorMode; }
        if (request.NETAssemblyTypeAcceleratorsSpecified) { EnsureBuildLibraries(); buildLibraries!.AssemblyTypeAccelerators = request.NETAssemblyTypeAccelerators; }
        if (request.NETAssemblyTypeAcceleratorAssembliesSpecified) { EnsureBuildLibraries(); buildLibraries!.AssemblyTypeAcceleratorAssemblies = request.NETAssemblyTypeAcceleratorAssemblies; }
        if (request.NETDoNotCopyLibrariesRecursivelySpecified) { EnsureBuildLibraries(); buildLibraries!.NETDoNotCopyLibrariesRecursively = request.NETDoNotCopyLibrariesRecursively; }

        if (buildLibraries is null)
            return null;

        if (enableBuildLibraries)
            buildLibraries.Enable = true;

        return new ConfigurationBuildLibrariesSegment { BuildLibraries = buildLibraries };
    }

    private static ConfigurationPlaceHolderOptionSegment? CreatePlaceHolderOptionsSegment(BuildConfigurationRequest request)
    {
        if (!request.SkipBuiltinReplacementsSpecified || !request.SkipBuiltinReplacements)
            return null;

        return new ConfigurationPlaceHolderOptionSegment
        {
            PlaceHolderOption = new PlaceHolderOptionConfiguration
            {
                SkipBuiltinReplacements = true
            }
        };
    }

    private static string? ResolveSecret(bool secretSpecified, string? secret, bool secretFileSpecified, string? secretFilePath)
    {
        if (secretFileSpecified && !string.IsNullOrWhiteSpace(secretFilePath))
            return File.ReadAllText(secretFilePath!.Trim()).Trim();

        if (secretSpecified && !string.IsNullOrWhiteSpace(secret))
            return secret!.Trim();

        return null;
    }
}
