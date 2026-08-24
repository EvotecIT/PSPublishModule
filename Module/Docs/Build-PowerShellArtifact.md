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
Build-PowerShellArtifact [-Path] <string> -Kind <PowerShellCompilationArtifactKind> [-OutputDirectory <string>] [-Name <string>] [-Mode <PowerShellCompilationMode>] [-TargetFramework <string>] [-RuntimeIdentifier <string>] [-SelfContained] [-SingleFile <bool>] [-Optimization <PowerShellCompilationExecutableOptimization>] [-SignArtifact] [-CertificateThumbprint <string>] [-CertificateStoreLocation <CertificateStoreLocation>] [-TimeStampServer <string>] [-SigningTimeoutSeconds <int>] [-KeepBuildWorkspace] [-TimeoutSeconds <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Builds a packaged executable, typed CLR library, or importable binary/hybrid module from PowerShell source.

## EXAMPLES

### EXAMPLE 1
```powershell
Build-PowerShellArtifact -Path .\tool.ps1 -Kind Executable -OutputDirectory .\artifacts
```


### EXAMPLE 2
```powershell
Build-PowerShellArtifact -Path .\module.psm1 -Kind BinaryModule -Mode Hybrid -OutputDirectory .\artifacts
```


## PARAMETERS

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
Artifact shape to produce.

```yaml
Type: PowerShellCompilationArtifactKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Executable, Library, BinaryModule

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Mode
Fallback policy. Defaults to Package for EXE, Strict for binary modules, and Hybrid for CLR libraries. Analyze is not a build mode.

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
PowerShell script or module source path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.PowerShellCompilationBuildResult`

## RELATED LINKS

- None
