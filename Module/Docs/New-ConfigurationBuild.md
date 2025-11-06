---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationBuild

## SYNOPSIS
Allows to configure build process for the module

## SYNTAX

```
New-ConfigurationBuild [-Enable] [-DeleteTargetModuleBeforeBuild] [-MergeModuleOnBuild]
 [-MergeFunctionsFromApprovedModules] [-SignModule] [-SignIncludeInternals] [-SignIncludeBinaries]
 [-SignIncludeExe] [[-SignCustomInclude] <String[]>] [[-SignExcludePaths] <String[]>] [-DotSourceClasses]
 [-DotSourceLibraries] [-SeparateFileLibraries] [-RefreshPSD1Only] [-UseWildcardForFunctions]
 [-LocalVersioning] [-VersionedInstallStrategy <String>] [-VersionedInstallKeep <Int32>] [-SkipBuiltinReplacements]
 [-DoNotAttemptToFixRelativePaths]
 [[-CertificateThumbprint] <String>] [[-CertificatePFXPath] <String>] [[-CertificatePFXBase64] <String>]
 [[-CertificatePFXPassword] <String>] [[-NETProjectPath] <String>] [[-NETConfiguration] <String>]
 [[-NETFramework] <String[]>] [[-NETProjectName] <String>] [-NETExcludeMainLibrary]
 [[-NETExcludeLibraryFilter] <String[]>] [[-NETIgnoreLibraryOnLoad] <String[]>] [[-NETBinaryModule] <String[]>]
 [-NETHandleAssemblyWithSameName] [-NETLineByLineAddType] [-NETBinaryModuleCmdletScanDisabled]
 [-NETMergeLibraryDebugging] [-NETResolveBinaryConflicts] [[-NETResolveBinaryConflictsName] <String>]
 [-NETBinaryModuleDocumentation] [-NETDoNotCopyLibrariesRecursively] [[-NETSearchClass] <String>]
 [-NETHandleRuntimes] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Allows to configure build process for the module

## EXAMPLES

### EXAMPLE 1
```
$newConfigurationBuildSplat = @{
    Enable                            = $true
    SignModule                        = $true
    MergeModuleOnBuild                = $true
    MergeFunctionsFromApprovedModules = $true
    CertificateThumbprint             = '483292C9E317AA1'
    NETResolveBinaryConflicts            = $true
    NETResolveBinaryConflictsName        = 'Transferetto'
    NETProjectName                    = 'Transferetto'
    NETConfiguration                  = 'Release'
    NETFramework                      = 'netstandard2.0'
    DotSourceLibraries                = $true
    DotSourceClasses                  = $true
    DeleteTargetModuleBeforeBuild     = $true
    VersionedInstallStrategy          = 'AutoRevision'
    VersionedInstallKeep              = 3
}
```

New-ConfigurationBuild @newConfigurationBuildSplat

## PARAMETERS

### -Enable
Enable build process

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -DeleteTargetModuleBeforeBuild
Delete target module before build

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -MergeModuleOnBuild
Merge module on build.
Combines Private/Public/Classes/Enums into a single PSM1 and prepares PSD1 accordingly.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -MergeFunctionsFromApprovedModules
When merging, also include functions from ApprovedModules referenced by the module.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignModule
Enables code-signing for the built module output.
When enabled alone, only merged
scripts are signed (psm1/psd1/ps1) and Internals are excluded.
Use the SignInclude*
switches to opt-in to additional content.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeInternals
When signing is enabled, also sign scripts that reside under the Internals folder.
Default: disabled (Internals are skipped).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeBinaries
When signing is enabled, include binary files (e.g., .dll, .cat) in signing.
Default: disabled.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignIncludeExe
When signing is enabled, include .exe files.
Default: disabled.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignCustomInclude
Overrides the include patterns passed to the signer.
If provided, this replaces
the defaults entirely.
Example: '*.psm1','*.psd1','*.ps1','*.dll'.
Use with
caution; it disables the default safe set.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SignExcludePaths
Additional path substrings to exclude from signing (relative matches).
Example:
'Examples','SomeFolder'.
Internals are excluded by default unless
-SignIncludeInternals is specified.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotSourceClasses
Keep classes in a separate dot-sourced file instead of merging them into the main PSM1.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotSourceLibraries
Keep library-loading code in a separate dot-sourced file.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SeparateFileLibraries
Write library-loading code into a distinct file and reference it via ScriptsToProcess/DotSource.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -RefreshPSD1Only
Only regenerate the manifest (PSD1) without rebuilding/merging other artifacts.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseWildcardForFunctions
Export all functions/aliases via wildcard in PSD1.
Useful for debugging non-merged builds.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -LocalVersioning
Use local versioning (bump PSD1 version on each build without querying PSGallery).

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipBuiltinReplacements
Skip builtin replacements option disables builtin replacements that are done by module builder.
This is useful if you use any of known replacements and you don't want them to be replaced by module builder.
This has to be used on the PSPublishModule by default, as it would break the module on publish.

Current known replacements are:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -DoNotAttemptToFixRelativePaths
Configures module builder to not replace $PSScriptRoot\..\ with $PSScriptRoot\
This is useful if you have a module that has a lot of relative paths that are required when using Private/Public folders,
but for merge process those are not supposed to be there as the paths change.
By default module builder will attempt to fix it.
This option disables this functionality.
Best practice is to use $MyInvocation.MyCommand.Module.ModuleBase or similar instead of relative paths.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateThumbprint
Thumbprint of a code-signing certificate from the local cert store to sign module files.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXPath
Path to a PFX containing a code-signing certificate used for signing.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXBase64
Base64 string of a PFX (e.g., provided via CI secrets) used for signing.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificatePFXPassword
Password for the PFX provided via -CertificatePFXPath or -CertificatePFXBase64.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETProjectPath
Path to the project that you want to build.
This is useful if it's not in Sources folder directly within module directory

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 7
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETConfiguration
Build configuration for .NET projects ('Release' or 'Debug').

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 8
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETFramework
Target frameworks for .NET build (e.g., 'netstandard2.0','net6.0').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 9
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETProjectName
By default it will assume same name as project name, but you can provide different name if needed.
It's required if NETProjectPath is provided

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 10
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETExcludeMainLibrary
Exclude main library from build, this is useful if you have C# project that you want to build
that is used mostly for generating libraries that are used in PowerShell module
It won't include main library in the build, but it will include all other libraries

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETExcludeLibraryFilter
Provide list of filters for libraries that you want to exclude from build, this is useful if you have C# project that you want to build, but don't want to include all libraries for some reason

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 11
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETIgnoreLibraryOnLoad
This is to exclude libraries from being loaded in PowerShell by PSM1/Librarties.ps1 files.
This is useful if you have a library that is not supposed to be loaded in PowerShell, but you still need it
For example library that's not NET based and is as dependency for other libraries

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 12
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModule
Provide list of binary modules that you want to import-module in the module.
This is useful if you're building a module that has binary modules and you want to import them in the module.
In here you provide one or more binrary module names that you want to import in the module.
Just the DLL name with extension without path.
Path is assumed to be $PSScriptRoot\Lib\Standard or $PSScriptRoot\Lib\Default or $PSScriptRoot\Lib\Core

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 13
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETHandleAssemblyWithSameName
Adds try/catch block to handle assembly with same name is already loaded exception and ignore it.
It's useful in PowerShell 7, as it's more strict about this than Windows PowerShell, and usually everything should work as expected.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: HandleAssemblyWithSameName

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETLineByLineAddType
Adds Add-Type line by line, this is useful if you have a lot of libraries and you want to see which one is causing the issue.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModuleCmdletScanDisabled
This is to disable scanning for cmdlets in binary modules, this is useful if you have a lot of binary modules and you don't want to scan them for cmdlets.
By default it will scan for cmdlets/aliases in binary modules and add them to the module PSD1/PSM1 files.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETMergeLibraryDebugging
Add special logic to simplify debugging of merged libraries, this is useful if you have a lot of libraries and you want to see which one is causing the issue.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: MergeLibraryDebugging

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETResolveBinaryConflicts
Add special logic to resolve binary conflicts.
It uses by defalt the project name.
If you want to use different name use NETResolveBinaryConflictsName

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: ResolveBinaryConflicts

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETResolveBinaryConflictsName
Add special logic to resolve binary conflicts for specific project name.

```yaml
Type: String
Parameter Sets: (All)
Aliases: ResolveBinaryConflictsName

Required: False
Position: 14
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETBinaryModuleDocumentation
Include documentation for binary modules, this is useful if you have a lot of binary modules and you want to include documentation for them (if available in XML format)

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: NETDocumentation, NETBinaryModuleDocumenation

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETDoNotCopyLibrariesRecursively
Do not copy libraries recursively.
Normally all libraries are copied recursively, but this option disables that functionality so it won't copy subfolders of libraries.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETSearchClass
Provide a name for class when using NETResolveBinaryConflicts or NETResolveBinaryConflictsName.
By default it uses \`$LibraryName.Initialize\` however that may not be always the case

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 15
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NETHandleRuntimes
Add special logic to handle runtimes.
It's useful if you have a library that is not supposed to be loaded in PowerShell, but you still need it
For example library that's not NET based and is as dependency for other libraries

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS
### -VersionedInstallStrategy
Controls how the module is installed into user Module roots after build.
Exact installs to `<Modules>\\Name\\<ModuleVersion>`. AutoRevision installs to `<ModuleVersion>.<n>` choosing the next free revision — recommended for dev runs to avoid folder-in-use issues.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: AutoRevision
Accept pipeline input: False
Accept wildcard characters: False
```

### -VersionedInstallKeep
How many versions to keep per module when using versioned installs. Older ones are pruned.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: 3
Accept pipeline input: False
Accept wildcard characters: False
```
