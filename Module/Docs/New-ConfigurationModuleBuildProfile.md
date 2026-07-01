---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationModuleBuildProfile
## SYNOPSIS
Emits a reusable module build profile for common PowerForge module builds.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationModuleBuildProfile [-Profile <ModuleBuildProfileKind>] [-Documentation <bool>] [-DocumentationPath <string>] [-DocumentationReadmePath <string>] [-SyncExternalHelpToProjectRoot <bool>] [-AboutTopicsSourcePath <string[]>] [-Validation <bool>] [-EnableScriptAnalyzer <bool>] [-FileConsistency <bool>] [-RequiredEncoding <FileConsistencyEncoding>] [-RequiredLineEnding <FileConsistencyLineEnding>] [-FileConsistencyExcludeDirectories <string[]>] [-EncodingOverrides <hashtable>] [-Compatibility <bool>] [-MinimumCompatibilityPercentage <int>] [-ImportSelf <bool>] [-ImportRequiredModules <bool>] [-MergeModuleOnBuild <bool>] [-MergeFunctionsFromApprovedModules <bool>] [-SignModule <bool>] [-CertificateThumbprint <string>] [-SkipBuiltinReplacements] [-DoNotAttemptToFixRelativePaths] [-DotSourceLibraries] [-DotSourceClasses] [-InstallMissingModules <bool>] [-VersionedInstallStrategy <InstallationStrategy>] [-VersionedInstallKeep <int>] [-KillLockersBeforeInstall] [-KillLockersForce] [-NETProjectPath <string>] [-NETProjectName <string>] [-NETConfiguration <string>] [-NETFramework <string[]>] [-NETHandleAssemblyWithSameName] [-NETAssemblyLoadContext] [-ResolveBinaryConflicts] [-ResolveBinaryConflictsName <string>] [-NETAssemblyTypeAcceleratorMode <AssemblyTypeAcceleratorExportMode>] [-NETAssemblyTypeAccelerators <string[]>] [-NETAssemblyTypeAcceleratorAssemblies <string[]>] [-SignIncludeBinaries] [-SignIncludeInternals] [-SignIncludeExe] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits the same configuration segment types as the lower-level
New-ConfigurationFormat, New-ConfigurationDocumentation,
New-ConfigurationValidation, New-ConfigurationFileConsistency,
New-ConfigurationCompatibility, New-ConfigurationImportModule, and
New-ConfigurationBuild commands. Use it when a module wrapper should stay thin
and only declare project-specific values.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationModuleBuildProfile
```


### EXAMPLE 2
```powershell
New-ConfigurationModuleBuildProfile -Profile Binary -NETProjectName MyModule -NETProjectPath Sources\MyModule
```


## PARAMETERS

### -AboutTopicsSourcePath
Source paths for about-topic files.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificateThumbprint
Code-signing certificate thumbprint.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Compatibility
Enable cross-version PowerShell compatibility defaults.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Documentation
Enable documentation generation defaults.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DocumentationPath
Generated documentation path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DocumentationReadmePath
Readme path used by generated documentation.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

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
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotSourceClasses
Keep classes dot-sourced.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DotSourceLibraries
Keep library-loading code dot-sourced.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnableScriptAnalyzer
Enable PSScriptAnalyzer as part of validation.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EncodingOverrides
Per-pattern encoding overrides for file consistency checks.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileConsistency
Enable file consistency defaults.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FileConsistencyExcludeDirectories
Directory names excluded from file consistency checks.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ImportRequiredModules
Import RequiredModules before validation/test steps that need them.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ImportSelf
Import the module under build before validation/test steps that need it.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallMissingModules
Install missing module dependencies on the build host.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

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
Possible values:

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
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MergeFunctionsFromApprovedModules
Merge referenced functions from approved modules.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MergeModuleOnBuild
Merge module source files during build.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MinimumCompatibilityPercentage
Minimum percentage of cross-compatible files.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETAssemblyLoadContext
Load binary module dependencies through an AssemblyLoadContext on PowerShell Core.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETAssemblyTypeAcceleratorAssemblies
Assembly names whose public types may be exposed as PowerShell type accelerators.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETAssemblyTypeAcceleratorMode
Controls type accelerator exposure for binary-module dependencies.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETAssemblyTypeAccelerators
Fully-qualified type names to expose as PowerShell type accelerators.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETConfiguration
.NET build configuration.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETFramework
.NET target frameworks to build.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETHandleAssemblyWithSameName
Handle assemblies with the same name during import.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETProjectName
Project name for binary-module builds.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NETProjectPath
Path to the owned .NET project for binary-module builds.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Named profile to emit.

```yaml
Type: ModuleBuildProfileKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Standard, Binary

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredEncoding
Required encoding for file consistency checks.

```yaml
Type: FileConsistencyEncoding
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: ASCII, UTF8, UTF8BOM, Unicode, BigEndianUnicode, UTF7, UTF32

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredLineEnding
Required line endings for file consistency checks.

```yaml
Type: FileConsistencyLineEnding
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CRLF, LF

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ResolveBinaryConflicts
Resolve binary dependency conflicts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ResolveBinaryConflictsName
Project name used when resolving binary dependency conflicts.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeBinaries
When signing is enabled, include binary files.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeExe
When signing is enabled, include executable files.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignIncludeInternals
When signing is enabled, include internal scripts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignModule
Enable signing for the built module.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipBuiltinReplacements
Skip built-in placeholder replacements during build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SyncExternalHelpToProjectRoot
Sync generated external help back to the project root.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Validation
Enable module validation defaults.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionedInstallKeep
Number of installed versions to keep.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionedInstallStrategy
Versioned install strategy.

```yaml
Type: InstallationStrategy
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Exact, AutoRevision

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
