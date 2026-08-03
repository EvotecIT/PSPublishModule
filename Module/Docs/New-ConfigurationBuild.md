---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationBuild
## SYNOPSIS
Allows configuring the build process for a module.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationBuild [-Enable] [-DeleteTargetModuleBeforeBuild] [-MergeModuleOnBuild] [-MergeFunctionsFromApprovedModules] [-SignModule] [-SignIncludeInternals] [-SignIncludeBinaries] [-SignIncludeExe] [-SignCustomInclude <string[]>] [-SignExcludePaths <string[]>] [-SignOverwriteSigned] [-DotSourceClasses] [-DotSourceLibraries] [-SeparateFileLibraries] [-RefreshPSD1Only] [-UseWildcardForFunctions] [-LocalVersioning] [-SyncNETProjectVersion] [-VersionedInstallStrategy <InstallationStrategy>] [-VersionedInstallKeep <int>] [-VersionedInstallLegacyFlatHandling <LegacyFlatModuleHandling>] [-VersionedInstallPreserveVersions <string[]>] [-InstallMissingModules] [-InstallMissingModulesForce] [-InstallMissingModulesPrerelease] [-ResolveMissingModulesOnline] [-WarnIfRequiredModulesOutdated] [-InstallMissingModulesRepository <string>] [-InstallMissingModulesCredentialUserName <string>] [-InstallMissingModulesCredentialSecret <string>] [-InstallMissingModulesCredentialSecretFilePath <string>] [-SkipBuiltinReplacements] [-DoNotAttemptToFixRelativePaths] [-CertificateThumbprint <string>] [-CertificatePFXPath <string>] [-CertificatePFXBase64 <string>] [-CertificatePFXPassword <string>] [-NETProjectPath <string>] [-NETConfiguration <string>] [-NETFramework <string[]>] [-NETProjectName <string>] [-NETExcludeMainLibrary] [-NETExcludeLibraryFilter <string[]>] [-NETIgnoreLibraryOnLoad <string[]>] [-NETBinaryModule <string[]>] [-NETHandleAssemblyWithSameName] [-NETLineByLineAddType] [-NETBinaryModuleCmdletScanDisabled] [-NETMergeLibraryDebugging] [-NETResolveBinaryConflicts] [-NETResolveBinaryConflictsName <string>] [-NETBinaryModuleDocumentation] [-NETDoNotCopyLibrariesRecursively] [-NETSearchClass <string>] [-NETHandleRuntimes] [-NETAssemblyLoadContext] [-NETDevelopmentBinaries] [-NETDevelopmentBinariesMode <ModuleDevelopmentBinaryMode>] [-NETDevelopmentBinariesPath <string>] [-NETDevelopmentBinariesEnvironmentVariable <string>] [-NETDevelopmentConfigurationEnvironmentVariable <string>] [-NETDevelopmentSourceBootstrapperMode <ModuleDevelopmentSourceBootstrapperMode>] [-NETAssemblyTypeAcceleratorMode <AssemblyTypeAcceleratorExportMode>] [-NETAssemblyTypeAccelerators <string[]>] [-NETAssemblyTypeAcceleratorAssemblies <string[]>] [-KillLockersBeforeInstall] [-KillLockersForce] [-AutoSwitchExactOnPublish] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits build configuration that is consumed by Invoke-ModuleBuild / Build-Module.
It controls how the module is merged, signed, versioned, installed, and how optional .NET publishing is performed.

Dependency-related options in this cmdlet affect the build machine, not artefact packaging. Use
InstallMissingModules when the build host needs missing RequiredModule or
ExternalModule dependencies installed before merge/import/test steps run.

If you want dependencies copied into ZIP/unpacked artefacts, configure that separately with
New-ConfigurationArtefact -AddRequiredModules. Build-time installation and artefact packaging are designed
as separate decisions because many teams want one without the other.

For a broader dependency workflow explanation, see about_ModuleDependencies.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationBuild -Enable -MergeModuleOnBuild -LocalVersioning -VersionedInstallStrategy AutoRevision -VersionedInstallKeep 3
```


### EXAMPLE 2
```powershell
New-ConfigurationBuild -Enable -SignModule -CertificateThumbprint '0123456789ABCDEF' -KillLockersBeforeInstall -KillLockersForce
```


### EXAMPLE 3
```powershell
New-ConfigurationBuild -Enable -InstallMissingModules -InstallMissingModulesRepository 'PSGallery'
```

Use this when the build host does not already have the declared RequiredModule or ExternalModule dependencies installed.

### EXAMPLE 4
```powershell
New-ConfigurationBuild -Enable -ResolveMissingModulesOnline -WarnIfRequiredModulesOutdated
```

Useful in CI or on clean machines when dependency versions should come from the repository rather than the local module cache.

### EXAMPLE 5
```powershell
New-ConfigurationBuild -Enable -InstallMissingModules -InstallMissingModulesRepository 'MyPrivateFeed' -InstallMissingModulesCredentialUserName 'build' -InstallMissingModulesCredentialSecretFilePath '.secrets\feed-token.txt'
```

Use the credential parameters only when the repository requires authentication.

## PARAMETERS

### -AutoSwitchExactOnPublish
Auto switch VersionedInstallStrategy to Exact when publishing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXBase64
Base64 string of a PFX containing a code-signing certificate.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXPassword
Password for the PFX provided via CertificatePFXPath or CertificatePFXBase64.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXPath
Path to a PFX containing a code-signing certificate.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateThumbprint
Thumbprint of a code-signing certificate from the local cert store.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DeleteTargetModuleBeforeBuild
Delete target module before build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DoNotAttemptToFixRelativePaths
Do not attempt to fix relative paths during merge.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotSourceClasses
Keep classes in a separate dot-sourced file instead of merging into the main PSM1.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotSourceLibraries
Keep library-loading code in a separate dot-sourced file.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Enable
Enable build process.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModules
Install missing module dependencies (RequiredModule/ExternalModule) before build. This affects
the build host only; it does not bundle modules into artefacts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesCredentialSecret
Credential secret or token for dependency installation. Prefer the file-path form in CI when you do not want
the secret value embedded directly in scripts.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesCredentialSecretFilePath
Path to a file containing the credential secret or token. This is often the safest option for automation and
CI agents.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesCredentialUserName
Credential user name for dependency installation. This is usually paired with
InstallMissingModulesCredentialSecret or InstallMissingModulesCredentialSecretFilePath.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesForce
Force re-install or update even if dependencies are already installed. Useful when you want the build host to
re-sync against the repository instead of accepting the current local state.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesPrerelease
Allow prerelease versions when installing dependencies. Use this only when the dependency declaration and
repository policy intentionally allow prerelease packages.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InstallMissingModulesRepository
Repository name used for dependency installation (defaults to PSGallery). Set this when your build
should resolve dependencies from a named private feed or alternate gallery.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -KillLockersBeforeInstall
Kill locking processes before install.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -KillLockersForce
Force killing locking processes before install.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LocalVersioning
Use local versioning (bump PSD1 version on each build without querying PSGallery).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MergeFunctionsFromApprovedModules
When merging, also include functions from ApprovedModules referenced by the module.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MergeModuleOnBuild
Merge module on build (combine Private/Public/Classes/Enums into one PSM1).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETAssemblyLoadContext
Load the binary module through a custom AssemblyLoadContext on PowerShell Core.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: UseAssemblyLoadContext
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETAssemblyTypeAcceleratorAssemblies
Assembly simple names whose public types may be exposed as PowerShell type accelerators when assembly mode is enabled.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: AssemblyTypeAcceleratorAssemblies
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETAssemblyTypeAcceleratorMode
Controls optional type accelerator exposure for dependency types loaded in the module AssemblyLoadContext.

```yaml
Type: AssemblyTypeAcceleratorExportMode
Parameter Sets: __AllParameterSets
Aliases: AssemblyTypeAcceleratorMode
Possible values: None, AllowList, Assembly, Enums

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETAssemblyTypeAccelerators
Fully-qualified dependency type names to expose as PowerShell type accelerators from the module AssemblyLoadContext.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: AssemblyTypeAccelerators
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModule
Binary module names (DLL file names) to import in the module.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModuleCmdletScanDisabled
Disable cmdlet scanning for the binary module.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModuleDocumentation
Enable binary module documentation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: NETDocumentation, NETBinaryModuleDocumenation
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETConfiguration
Build configuration for .NET projects (Release or Debug).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentBinaries
Generate checked-in source bootstrapper logic for local development binary outputs.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentBinariesEnvironmentVariable
Optional environment variable used by Environment development-binary mode.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentBinariesMode
Controls when the generated source bootstrapper loads local development binaries.

```yaml
Type: ModuleDevelopmentBinaryMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Off, Environment, Auto

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentBinariesPath
Optional root folder that contains configuration/framework development binary outputs.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentConfigurationEnvironmentVariable
Optional environment variable that chooses the development build configuration.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDevelopmentSourceBootstrapperMode
Controls how the source PSM1 is maintained when development binary bootstrapping is enabled.

```yaml
Type: ModuleDevelopmentSourceBootstrapperMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: PreserveSingleFile, ReplaceSingleFile

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDoNotCopyLibrariesRecursively
Do not copy libraries recursively (legacy option).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETExcludeLibraryFilter
Filters for libraries that should be excluded from build output.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETExcludeMainLibrary
Exclude main library from build output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETFramework
Target frameworks for .NET build.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETHandleAssemblyWithSameName
Handle 'assembly with same name is already loaded' by wrapping Add-Type logic.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: HandleAssemblyWithSameName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETHandleRuntimes
Handle runtimes folder when copying libraries.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETIgnoreLibraryOnLoad
Exclude libraries from being loaded by PSM1/Libraries.ps1.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETLineByLineAddType
Add-Type libraries line by line (legacy debugging option).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETMergeLibraryDebugging
Debug DLL merge (legacy setting).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: MergeLibraryDebugging
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETProjectName
Project name for the .NET project (required when NETProjectPath is provided).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETProjectPath
Path to the .NET project to build (useful when not in Sources folder).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETResolveBinaryConflicts
Enable resolving binary conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: ResolveBinaryConflicts
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETResolveBinaryConflictsName
Project name used when resolving binary conflicts.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ResolveBinaryConflictsName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETSearchClass
Search class (legacy option).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RefreshPSD1Only
Only regenerate the manifest (PSD1) without rebuilding/merging other artefacts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ResolveMissingModulesOnline
Resolve Auto/Latest dependency versions from the repository without installing.
When not explicitly set, this is auto-enabled if any RequiredModules use Auto/Latest/Guid Auto.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SeparateFileLibraries
Write library-loading code into a distinct file and reference it via ScriptsToProcess/DotSource.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignCustomInclude
Override include patterns passed to the signer.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignExcludePaths
Additional path substrings to exclude from signing.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeBinaries
When signing is enabled, binaries are signed by default (e.g., .dll, .cat).
Use -SignIncludeBinaries:$false to opt out.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeExe
When signing is enabled, include .exe files in signing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeInternals
When signing is enabled, also sign scripts that reside under the Internals folder.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignModule
Enable code-signing for the built module output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignOverwriteSigned
When signing is enabled, overwrite existing signatures (re-sign files).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipBuiltinReplacements
Disables built-in replacements done by the module builder.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SyncNETProjectVersion
Synchronize the source .NET project version with the resolved module/manifest version before staging.
This is opt-in and updates the source .csproj file when a project path can be resolved.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseWildcardForFunctions
Export all functions/aliases via wildcard in PSD1.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -VersionedInstallKeep
How many versions to keep per module when using versioned installs.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -VersionedInstallLegacyFlatHandling
How to handle legacy flat module installs during install.

```yaml
Type: LegacyFlatModuleHandling
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Convert, Delete, Ignore

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -VersionedInstallPreserveVersions
Version folders to preserve during install pruning (for example older major versions).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -VersionedInstallStrategy
Controls how the module is installed into user Module roots after build.

```yaml
Type: InstallationStrategy
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Exact, AutoRevision

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WarnIfRequiredModulesOutdated
Warn if RequiredModule entries are older than the latest version available in the repository. This is a
reporting hint and does not change the manifest or install anything by itself.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `None`

## RELATED LINKS

- None
