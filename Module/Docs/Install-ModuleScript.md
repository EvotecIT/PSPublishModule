---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-ModuleScript
## SYNOPSIS
Copies only PowerShell scripts from a module's Internals\Scripts folder to a destination path.
The destination is flattened (no Module/Version subfolders).

## SYNTAX
### ByName (Default)
```powershell
Install-ModuleScript [-Name] <string> [-Path] <string> [-RequiredVersion <version>] [-ScriptsRelativePath <string>] [-Include <string[]>] [-Exclude <string[]>] [-OnExists <OnExistsOption>] [-Force] [-ListOnly] [-Unblock] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### ByModule
```powershell
Install-ModuleScript [-Path] <string> -Module <psobject> [-RequiredVersion <version>] [-ScriptsRelativePath <string>] [-Include <string[]>] [-Exclude <string[]>] [-OnExists <OnExistsOption>] [-Force] [-ListOnly] [-Unblock] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Copies only PowerShell scripts from a module's Internals\Scripts folder to a destination path.
The destination is flattened (no Module/Version subfolders).

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Install-ModuleScript -Name EFAdminManager -Path 'C:\Tools' -Verbose
```

Copies PowerShell script files under Internals\Scripts recursively into C:\Tools, preserving subfolders. Shows each copied file.

### EXAMPLE 2
```powershell
PS> Install-ModuleScript -Name EFAdminManager -Path 'C:\Tools' -Include 'Repair-*' -Unblock -OnExists Overwrite
```

Limits to files that start with Repair-, removes Windows Zone.Identifier (on Windows), and overwrites existing files.

### EXAMPLE 3
```powershell
PS> Get-Module -ListAvailable EFAdminManager | Install-ModuleScript -Path 'C:\Tools' -ListOnly
```

Shows Source, Destination, and chosen action for each file without writing anything.

## PARAMETERS

### -Exclude
Wildcard exclude filters (relative to the scripts folder).

```yaml
Type: String[]
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force overwriting read-only files when -OnExists Overwrite or -OnExists Merge with -Force for new-only behavior.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Include
Wildcard include filters (relative to the scripts folder). Defaults to '*.ps1'.

```yaml
Type: String[]
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ListOnly
Show which files would be copied without performing any changes.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Module
Specific module instance to source scripts from.

```yaml
Type: PSObject
Parameter Sets: ByModule
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -Name
Module name to source scripts from (highest installed version is used by default).

```yaml
Type: String
Parameter Sets: ByName
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -OnExists
Conflict handling when a destination file already exists. Defaults to Merge (keep existing).

```yaml
Type: OnExistsOption
Parameter Sets: ByName, ByModule
Aliases: None
Possible values: Merge, Overwrite, Skip, Stop, Refresh

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Destination folder to copy scripts into (created if missing).

```yaml
Type: String
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredVersion
Optional exact module version to use when multiple versions are installed.

```yaml
Type: Version
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptsRelativePath
Relative path under the module root where scripts live. Defaults to 'Internals\Scripts'.

```yaml
Type: String
Parameter Sets: ByName, ByModule
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Unblock
When set, attempts to remove the Windows Zone.Identifier (unblock) on copied files.

```yaml
Type: SwitchParameter
Parameter Sets: ByName, ByModule
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

- `System.String
System.Management.Automation.PSObject`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None
