---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ModuleRepositoryBootstrap
## SYNOPSIS
Creates a managed workstation bootstrap package for private module repository onboarding.

## SYNTAX
### __AllParameterSets
```powershell
New-ModuleRepositoryBootstrap [[-ProfileName] <string[]>] [-OutputDirectory] <string> [-ScriptName <string>] [-ProfileFileName <string>] [-InstallModule <string[]>] [-Force] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
The generated package contains a non-secret profile JSON file and a PowerShell script that imports the profile,
installs requested prerequisites, connects to the private gallery, and optionally installs approved modules through
Install-PrivateModule. The package does not contain PATs, passwords, Entra tokens, or Azure Artifacts
Credential Provider session caches.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ModuleRepositoryBootstrap -ProfileName Company -OutputDirectory .\CompanyGallery -InstallModule ModuleA, ModuleB -Force
```

Writes profiles.json and Initialize-PrivateGallery.ps1 for managed desktop rollout.

## PARAMETERS

### -Force
Overwrite existing generated files.

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

### -InstallModule
Optional module names pre-populated into the generated bootstrap script.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputDirectory
Destination directory for the generated bootstrap package.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileFileName
Generated non-secret profile JSON file name.

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

### -ProfileName
Optional saved profile names to include. When omitted, all saved profiles are included.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Name, Profile
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Profile store scope to read. The default reads user profiles first, then machine-wide profiles.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptName
Generated bootstrap script file name.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ModuleRepositoryBootstrapScriptPackage`

## RELATED LINKS

- None
