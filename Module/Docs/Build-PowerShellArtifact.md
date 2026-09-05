---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Build-PowerShellArtifact
## SYNOPSIS
Builds a packaged executable, typed CLR library, or importable binary/hybrid module from PowerShell source.

## SYNTAX
### __AllParameterSets
```powershell
Build-PowerShellArtifact [-Path] <string[]> [-EntryPoint <string>] [-Kind <PowerShellCompilationArtifactKind>] [-OutputDirectory <string>] [-Name <string>] [-Mode <PowerShellCompilationMode>] [-ResourceMode <PowerShellCompilationResourceMode>] [-IncludeResource <string[]>] [-ExcludeResource <string[]>] [-TargetFramework <string>] [-RuntimeIdentifier <string>] [-SelfContained] [-SingleFile <bool>] [-Optimization <PowerShellCompilationExecutableOptimization>] [-TargetContract <PowerShellCompilationTargetContract>] [-UseBuildCache <bool>] [-BuildCacheDirectory <string>] [-SignArtifact] [-CertificateThumbprint <string>] [-CertificateStoreLocation <CertificateStoreLocation>] [-TimeStampServer <string>] [-SigningTimeoutSeconds <int>] [-KeepBuildWorkspace] [-EmitSource] [-EmitIrSnapshots] [-ExpectedPublicAbiSha256 <string>] [-TimeoutSeconds <int>] [-DependencyLock <PowerShellCompilationDependencyGraph>] [-AllowUnreviewedDependencies] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Builds a packaged executable, typed CLR library, or importable binary/hybrid module from PowerShell source.

## EXAMPLES

### EXAMPLE 1
```powershell
$lock = (powerforge powershell analyze .\MyModule --output json | ConvertFrom-Json).result.dependencyGraph; Build-PowerShellArtifact -Path .\MyModule -EmitSource -DependencyLock $lock
```


### EXAMPLE 2
```powershell
Build-PowerShellArtifact -Path .\tool.ps1 -AllowUnreviewedDependencies
```


### EXAMPLE 3
```powershell
Build-PowerShellArtifact -Path .\Public\Get-One.ps1, .\Public\Get-Two.ps1 -Kind BinaryModule -AllowUnreviewedDependencies
```


## PARAMETERS

### -AllowUnreviewedDependencies
Explicitly allow a development build to resolve dependencies without a separately reviewed lock.

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

### -BuildCacheDirectory
Optional machine-local content-addressed build-cache root.

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

### -CertificateStoreLocation
Certificate store used for Authenticode signing.

```yaml
Type: CertificateStoreLocation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, LocalMachine

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateThumbprint
Optional code-signing certificate thumbprint.

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

### -DependencyLock
Dependency graph produced by analysis and reviewed before this build.

```yaml
Type: PowerShellCompilationDependencyGraph
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EmitIrSnapshots
Publish a redacted semantic-only bound/lowered IR snapshot beside canonical evidence.

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

### -EmitSource
Publish an independently buildable generated C# source project beside the artifact.

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

### -EntryPoint
Explicit root .ps1 application entrypoint when several script paths are supplied for an executable.

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

### -ExcludeResource
Contained resource paths or glob patterns to exclude from optional payload.

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

### -ExpectedPublicAbiSha256
Reviewed public ABI SHA-256 that the generated artifact must match.

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

### -IncludeResource
Contained resource paths or glob patterns to include beside the artifact.

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

### -KeepBuildWorkspace
Retain the generated project workspace for inspection.

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

### -Kind
Optional artifact shape. Defaults to Executable for .ps1 and BinaryModule for module inputs.

```yaml
Type: PowerShellCompilationArtifactKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Executable, Library, BinaryModule

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Mode
Fallback policy. Defaults to Package for executables and Hybrid for module/library inputs. Analyze is not a build mode.

```yaml
Type: PowerShellCompilationMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Analyze, Package, Hybrid, Strict

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Name
Artifact file and assembly name. Defaults to the source file name.

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

### -Optimization
Optional trimmed or native-AOT publication for a Strict typed executable.

```yaml
Type: PowerShellCompilationExecutableOptimization
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, Trimmed, NativeAot

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputDirectory
Destination for durable artifacts and the compilation manifest.

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

### -Path
One PowerShell script/module path, or several loose .ps1 files for a typed library or strict binary module.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ResourceMode
Optional payload policy. Declared includes manifest, explicit, and safely inferred resources.

```yaml
Type: PowerShellCompilationResourceMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Declared, CompleteModule, None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RuntimeIdentifier
Optional runtime identifier used when publishing an executable.

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

### -SelfContained
Include the .NET runtime when publishing an executable.

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

### -SignArtifact
Authenticode-sign generated signable files before integrity hashes are recorded.

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

### -SigningTimeoutSeconds
Maximum time allowed for Authenticode signing.

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

### -SingleFile
Publish an executable as one file.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TargetContract
Explicit semantic, execution, and deployment target. Its kind and mode must match the resolved input.

```yaml
Type: PowerShellCompilationTargetContract
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TargetFramework
Generated .NET target framework.

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

### -TimeoutSeconds
Maximum restore and compile time in seconds.

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

### -TimeStampServer
RFC3161 timestamp service used for Authenticode signing.

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

### -UseBuildCache
Use the verified content-addressed generated-build cache.

```yaml
Type: Boolean
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

- `PowerForge.PowerShellCompilationBuildResult`

## RELATED LINKS

- None
