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
New-ConfigurationBuild [-Enable] [-DeleteTargetModuleBeforeBuild] [-MergeModuleOnBuild] [-MergeFunctionsFromApprovedModules] [-SignModule] [-SignIncludeInternals] [-SignIncludeBinaries] [-SignIncludeExe] [-SignCustomInclude <string[]>] [-SignExcludePaths <string[]>] [-SignOverwriteSigned] [-DotSourceClasses] [-DotSourceLibraries] [-SeparateFileLibraries] [-RefreshPSD1Only] [-UseWildcardForFunctions] [-LocalVersioning] [-VersionedInstallStrategy <InstallationStrategy>] [-VersionedInstallKeep <int>] [-InstallMissingModules] [-InstallMissingModulesForce] [-InstallMissingModulesPrerelease] [-InstallMissingModulesRepository <string>] [-InstallMissingModulesCredentialUserName <string>] [-InstallMissingModulesCredentialSecret <string>] [-InstallMissingModulesCredentialSecretFilePath <string>] [-SkipBuiltinReplacements] [-DoNotAttemptToFixRelativePaths] [-CertificateThumbprint <string>] [-CertificatePFXPath <string>] [-CertificatePFXBase64 <string>] [-CertificatePFXPassword <string>] [-NETProjectPath <string>] [-NETConfiguration <string>] [-NETFramework <string[]>] [-NETProjectName <string>] [-NETExcludeMainLibrary] [-NETExcludeLibraryFilter <string[]>] [-NETIgnoreLibraryOnLoad <string[]>] [-NETBinaryModule <string[]>] [-NETHandleAssemblyWithSameName] [-NETLineByLineAddType] [-NETBinaryModuleCmdletScanDisabled] [-NETMergeLibraryDebugging] [-NETResolveBinaryConflicts] [-NETResolveBinaryConflictsName <string>] [-NETBinaryModuleDocumentation] [-NETDoNotCopyLibrariesRecursively] [-NETSearchClass <string>] [-NETHandleRuntimes] [-KillLockersBeforeInstall] [-KillLockersForce] [-AutoSwitchExactOnPublish] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits build configuration that is consumed by Invoke-ModuleBuild / Build-Module.
It controls how the module is merged, signed, versioned, installed, and how optional .NET publishing is performed.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationBuild -Enable -MergeModuleOnBuild -LocalVersioning -VersionedInstallStrategy AutoRevision -VersionedInstallKeep 3
```

### EXAMPLE 2
```powershell
New-ConfigurationBuild -Enable -SignModule -CertificateThumbprint '0123456789ABCDEF' -KillLockersBeforeInstall -KillLockersForce
```

## PARAMETERS

### -AutoSwitchExactOnPublish
Auto switch VersionedInstallStrategy to Exact when publishing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificatePFXBase64
Base64 string of a PFX containing a code-signing certificate.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificatePFXPassword
Password for the PFX provided via CertificatePFXPath or CertificatePFXBase64.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificatePFXPath
Path to a PFX containing a code-signing certificate.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificateThumbprint
Thumbprint of a code-signing certificate from the local cert store.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DeleteTargetModuleBeforeBuild
Delete target module before build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DoNotAttemptToFixRelativePaths
Do not attempt to fix relative paths during merge.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotSourceClasses
Keep classes in a separate dot-sourced file instead of merging into the main PSM1.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotSourceLibraries
Keep library-loading code in a separate dot-sourced file.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enable
Enable build process.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModules
Install missing module dependencies (Required/External) before build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesCredentialSecret
Credential secret/token for dependency installation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesCredentialSecretFilePath
Path to a file containing the credential secret/token.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesCredentialUserName
Credential user name for dependency installation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesForce
Force re-install even if dependencies are already installed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesPrerelease
Allow prerelease versions when installing dependencies.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModulesRepository
Repository name used for dependency installation (defaults to PSGallery).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KillLockersBeforeInstall
Kill locking processes before install.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KillLockersForce
Force killing locking processes before install.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LocalVersioning
Use local versioning (bump PSD1 version on each build without querying PSGallery).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MergeFunctionsFromApprovedModules
When merging, also include functions from ApprovedModules referenced by the module.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MergeModuleOnBuild
Merge module on build (combine Private/Public/Classes/Enums into one PSM1).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETBinaryModule
Binary module names (DLL file names) to import in the module.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETBinaryModuleCmdletScanDisabled
Disable cmdlet scanning for the binary module.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETBinaryModuleDocumentation
Enable binary module documentation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: NETDocumentation, NETBinaryModuleDocumenation

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETConfiguration
Build configuration for .NET projects (Release or Debug).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETDoNotCopyLibrariesRecursively
Do not copy libraries recursively (legacy option).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETExcludeLibraryFilter
Filters for libraries that should be excluded from build output.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETExcludeMainLibrary
Exclude main library from build output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETFramework
Target frameworks for .NET build.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETHandleAssemblyWithSameName
Handle 'assembly with same name is already loaded' by wrapping Add-Type logic.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: HandleAssemblyWithSameName

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETHandleRuntimes
Handle runtimes folder when copying libraries.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETIgnoreLibraryOnLoad
Exclude libraries from being loaded by PSM1/Libraries.ps1.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETLineByLineAddType
Add-Type libraries line by line (legacy debugging option).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETMergeLibraryDebugging
Debug DLL merge (legacy setting).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: MergeLibraryDebugging

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETProjectName
Project name for the .NET project (required when NETProjectPath is provided).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETProjectPath
Path to the .NET project to build (useful when not in Sources folder).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETResolveBinaryConflicts
Enable resolving binary conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: ResolveBinaryConflicts

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETResolveBinaryConflictsName
Project name used when resolving binary conflicts.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ResolveBinaryConflictsName

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETSearchClass
Search class (legacy option).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RefreshPSD1Only
Only regenerate the manifest (PSD1) without rebuilding/merging other artefacts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SeparateFileLibraries
Write library-loading code into a distinct file and reference it via ScriptsToProcess/DotSource.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignCustomInclude
Override include patterns passed to the signer.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignExcludePaths
Additional path substrings to exclude from signing.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeBinaries
When signing is enabled, binaries are signed by default (e.g., .dll, .cat).
Use -SignIncludeBinaries:$false to opt out.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeExe
When signing is enabled, include .exe files in signing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeInternals
When signing is enabled, also sign scripts that reside under the Internals folder.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignModule
Enable code-signing for the built module output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignOverwriteSigned
When signing is enabled, overwrite existing signatures (re-sign files).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipBuiltinReplacements
Disables built-in replacements done by the module builder.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseWildcardForFunctions
Export all functions/aliases via wildcard in PSD1.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionedInstallKeep
How many versions to keep per module when using versioned installs.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionedInstallStrategy
Controls how the module is installed into user Module roots after build.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None

